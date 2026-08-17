using System.Collections.Concurrent;
using System.Net.Sockets;
using VoxelEngine.Net;

namespace VoxelEngine.Server;

public class ServerConnection
{
    public readonly ConcurrentQueue<Packet> Inbox = new();
    public volatile bool Connected = true;
    public string? KickReason;

    /// When the socket was accepted, so a peer that connects and then says nothing can be dropped
    /// instead of holding a thread and a queue slot forever.
    public readonly long AcceptedAt = Environment.TickCount64;

    private readonly TcpClient mClient;
    private readonly NetStream mIn, mOut;

    /// Serialized packets waiting for the writer thread. This queue is the whole point: without it
    /// Send wrote to the socket from the tick thread, so one client that stopped reading filled its
    /// send buffer and blocked the tick - freezing every other player on the server.
    private readonly BlockingCollection<byte[]> mOutbox = new(new ConcurrentQueue<byte[]>());
    private long mPendingBytes;

    /// A client that queues this many unprocessed packets is flooding us: the tick loop drains a
    /// bounded number per tick, so an unbounded inbox is a memory leak one peer can trigger.
    private const int MAX_INBOX = 1024;

    /// The outbound counterpart. A player loading chunks legitimately sits well under this; a client
    /// that has stopped reading altogether hits it in seconds and gets dropped instead of growing
    /// the queue until the server runs out of memory.
    private const long MAX_PENDING_BYTES = 8 * 1024 * 1024;

    public ServerConnection(TcpClient client)
    {
        this.mClient = client;

        // Backstop for a peer that reads just slowly enough to stay under the byte cap: the writer
        // thread gives up on a single blocked write rather than parking there forever.
        client.SendTimeout = 30_000;

        var stream = client.GetStream();

        // Buffered, or every WriteByte/ReadByte in NetStream is its own socket syscall - a packet
        // header alone cost five. One BufferedStream per direction: sharing one for both would
        // interleave the read and write buffers over the same duplex socket.
        mIn = new NetStream(new BufferedStream(stream, 8192));
        mOut = new NetStream(new BufferedStream(stream, 8192));

        // Captured now, not read on demand: RemoteEndPoint throws once the socket is closed, and the
        // disconnect log reads it after exactly that.
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString();
    }

    /// Name from the Handshake, held until the LoginRequest that follows it arrives on a later tick.
    public string? PendingName { get; set; }

    public string? RemoteEndPoint { get; }

    private volatile bool mStarted;

    public void Start()
    {
        mStarted = true;
        new Thread(ReadLoop) { IsBackground = true, Name = "NetRead" }.Start();
        new Thread(WriteLoop) { IsBackground = true, Name = "NetWrite" }.Start();
    }

    private void ReadLoop()
    {
        try
        {
            // Must loop: beta's join is Handshake THEN LoginRequest, so reading a single packet and
            // falling into the finally below would close every connection before it could log in.
            while (Connected)
            {
                if (Inbox.Count >= MAX_INBOX)
                {
                    Kick("Too many packets queued");
                    return;
                }

                Inbox.Enqueue(Packet.Read(mIn));
            }
        }
        catch (EndOfStreamException)
        {
            // The ordinary case - the client closed the socket. Reported plainly, because
            // "Attempted to read past the end of the stream" would otherwise be the message on
            // every single disconnect.
            Kick("disconnect");
        }
        catch (IOException e) when (e.InnerException is SocketException)
        {
            // Client killed rather than closed: connection reset. Also routine.
            Kick("disconnect");
        }
        catch (Exception e)
        {
            Kick(e.Message);
        }
        finally
        {
            Connected = false;
        }
    }

    /// The only thread that touches the outgoing socket. Blocking here is harmless - it is this
    /// thread's job - which is exactly what makes Send safe to call from the tick loop.
    private void WriteLoop()
    {
        try
        {
            foreach (var bytes in mOutbox.GetConsumingEnumerable())
            {
                mOut.WriteBytes(bytes);
                Interlocked.Add(ref mPendingBytes, -bytes.Length);

                // Flush only once caught up, so a tick's worth of packets leaves as a few segments
                // rather than one per packet.
                if (mOutbox.Count == 0)
                    mOut.Flush();
            }

            mOut.Flush();
        }
        catch (Exception e)
        {
            Kick(e.Message);
        }
        finally
        {
            // Whoever gets here last closes the socket: the kick packet, if there was one, has now
            // either gone out or failed trying.
            mClient.Close();
        }
    }

    public void Send(PacketId id, Action<NetStream> write)
    {
        if (!Connected)
            return;

        byte[] bytes;
        try
        {
            // Serialized here on the caller's thread, queued for the writer. The cost is one small
            // buffer per packet; the benefit is that a stalled peer can never stall the tick.
            var buffer = new MemoryStream(64);
            var w = new NetStream(buffer);
            w.WritePacketId(id);
            write(w);
            w.Flush();
            bytes = buffer.ToArray();
        }
        catch (Exception e)
        {
            Kick(e.Message);                                 // a packet writer that throws, e.g. an over-long string
            return;
        }

        if (Interlocked.Read(ref mPendingBytes) + bytes.Length > MAX_PENDING_BYTES)
        {
            Kick("Client can't keep up");
            return;
        }

        Interlocked.Add(ref mPendingBytes, bytes.Length);
        Enqueue(bytes);
    }

    /// Adding after CompleteAdding throws; a connection being torn down races exactly that.
    private void Enqueue(byte[] bytes)
    {
        try
        {
            mOutbox.Add(bytes);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Kick(string reason)
    {
        if (!Connected)
            return;

        KickReason = reason;

        // Down before the write, not after: Send calls Kick when a write fails, so leaving this
        // true here means Kick -> Send -> Kick -> ... until the stack overflows. That fires on every
        // normal disconnect, because the read loop kicks with a socket that is already dead.
        Connected = false;

        // Queued like any other packet rather than written here: writing inline would put the tick
        // thread back on the socket it is being kicked off, which is the block this class exists to
        // avoid. The writer thread sends it, then closes.
        try
        {
            var buffer = new MemoryStream(64);
            var w = new NetStream(buffer);
            w.WritePacketId(PacketId.DisconnectKick);
            w.WriteString(reason);
            w.Flush();

            if (mStarted)
            {
                Enqueue(buffer.ToArray());
            }
            else
            {
                // Refused before Start (the server was busy), so there is no writer thread to hand
                // it to. Inline is safe here: this is the accept thread, and SendTimeout bounds it.
                mOut.WriteBytes(buffer.ToArray());
                mOut.Flush();
            }
        }
        catch
        {
            // The connection is already going away; there is nothing left to salvage.
        }

        mOutbox.CompleteAdding();

        // With a writer thread running, it closes the socket once it has drained the kick packet.
        if (!mStarted)
            mClient.Close();
    }
}
