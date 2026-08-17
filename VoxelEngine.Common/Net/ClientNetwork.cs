using System.Collections.Concurrent;
using System.Net.Sockets;

namespace VoxelEngine.Net;

public sealed class ClientNetwork
{
    public readonly ConcurrentQueue<Packet> Inbox = new();
    public bool Connected { get; private set; }
    public string? DisconnectReason { get; private set; }
    public int LocalEntityId { get; private set; }

    private TcpClient mClient;
    private NetStream mIn, mOut;
    private readonly object mWriteLock = new();

    public async Task<bool> ConnectAsync(string address, string username)
    {
        try
        {
            var (host, port) = ParseAddress(address);

            mClient = new TcpClient();
            await mClient.ConnectAsync(host, port);
            mClient.NoDelay = true;

            // Buffered per direction - unbuffered, every NetStream.WriteByte/ReadByte was a separate
            // socket syscall, and with NoDelay set, often its own TCP segment.
            var stream = mClient.GetStream();
            mIn = new NetStream(new BufferedStream(stream, 8192));
            mOut = new NetStream(new BufferedStream(stream, 8192));
            Connected = true;
            
            // Step 1: handshake. Server replies "-" for offline mode; we ignore the value entirely, since we have no session to verify.
            
            Send(PacketId.Handshake, w => w.WriteString(username));
            if (mIn.ReadPacketId() != PacketId.Handshake)
            {
                Fail("Protocol error");
                return false;
            }
            mIn.ReadString();

            // Step 2: login request.
            Send(PacketId.LoginRequest, w =>
            {
                w.WriteInt(14);
                w.WriteString(username);
                w.WriteLong(0); // Seed
                w.WriteByte(0); // Dimension, both ignored by the server
            });

            var reply = mIn.ReadPacketId();
            if (reply == PacketId.DisconnectKick)
            {
                Fail(mIn.ReadString());
                return false;
            }

            if (reply != PacketId.LoginRequest)
            {
                Fail("Protocol error");
                return false;
            }

            LocalEntityId = mIn.ReadInt();
            mIn.ReadString();
            mIn.ReadLong();
            mIn.ReadByte();

            new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "ClientNet"
            }.Start();
            return true;
        }
        catch (Exception e)
        {
            Fail(e.Message);
            return false;
        }
    }

    private void ReadLoop()
    {
        try
        {
            while (Connected)
            {
                var packet = Packet.Read(mIn!); // same shape table the server uses

                // Handled here rather than queued: a kick means the socket is already dead, and the reason has to reach the UI even if the game loop never drains the queue again.
                if (packet.Id == PacketId.DisconnectKick)
                {
                    Fail(packet.OpenBody().ReadString());
                    return;
                }

                Inbox.Enqueue(packet);
            }
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    public void Send(PacketId id, Action<NetStream> write)
    {
        if (!Connected)
            return;

        try
        {
            lock (mWriteLock)
            {
                mOut!.WritePacketId(id);
                write(mOut);
                mOut.Flush();
            }
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    private void Fail(string reason)
    {
        DisconnectReason ??= reason;
        Connected = false;
        mClient?.Close();
    }

    private static (string host, int port) ParseAddress(string address)
    {
        const int DEFAULT_PORT = 25565;

        int colon = address.LastIndexOf(':');
        if (colon < 0)
            return (address.Trim(), DEFAULT_PORT);

        string host = address[..colon].Trim();
        return (host, int.TryParse(address[(colon + 1)..].Trim(), out var p) ? p : DEFAULT_PORT);
    }
    
    public void Disconnect() { Fail("Disconnected"); }
}