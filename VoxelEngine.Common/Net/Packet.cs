// One packet as it came off the wire: id plus buffered body. | Stage 3

namespace VoxelEngine.Net;

/// <summary>
/// A packet fully consumed from the socket but not yet interpreted. Buffering the body is what lets
/// a handler run later (or on another thread) without the socket having to stay at the right byte -
/// and a handler that misreads its own packet then corrupts only itself, not the connection.
/// </summary>
public readonly struct Packet
{
    public readonly PacketId Id;

    /// <summary>Fields in wire order, big-endian. Empty for KeepAlive.</summary>
    public readonly byte[] Body;

    public Packet(PacketId id, byte[] body)
    {
        Id = id;
        Body = body;
    }

    /// <summary>Reads one whole packet, blocking until all of it arrives. This is the read loop for both ends.</summary>
    public static Packet Read(NetStream s)
    {
        // Two statements, not one: ReadBody needs the id first, and relying on argument evaluation
        // order for that would be load-bearing and invisible.
        var id = s.ReadPacketId();
        return new Packet(id, PacketLayout.ReadBody(id, s));
    }

    /// <summary>Reader over the buffered body. Read fields back in PacketLayout's order for this id - that table is the schema.</summary>
    public NetStream OpenBody() => new(new MemoryStream(Body, writable: false));

    public override string ToString() => $"{Id} (0x{(byte)Id:X2}, {Body.Length} byte body)";
}
