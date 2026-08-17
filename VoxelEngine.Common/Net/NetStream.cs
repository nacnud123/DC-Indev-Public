using System.Text;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.Net;

public sealed class NetStream
{
    private readonly Stream mStream;

    public NetStream(Stream stream)
    {
        mStream = stream;
    }

    // --- reading ---
    public byte ReadByte()
    {
        int b = mStream.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return (byte)b;
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();
    public bool ReadBool() => ReadByte() != 0;

    public short ReadShort()
    {
        var b = ReadExact(2);
        return (short)((b[0] << 8) | b[1]);
    }

    public int ReadInt()
    {
        var b = ReadExact(4);
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    public long ReadLong()
    {
        var b = ReadExact(8);
        long v = 0;
        for (int i = 0; i < 8; i++) v = (v << 8) | b[i];
        return v;
    }

    public float ReadFloat()
    {
        var b = ReadExact(4);
        Array.Reverse(b);
        return BitConverter.ToSingle(b);
    }

    public double ReadDouble()
    {
        var b = ReadExact(8);
        Array.Reverse(b);
        return BitConverter.ToDouble(b);
    }

    /// Beta "string16": short length, then that many UTF-16 big-endian chars.
    public string ReadString()
    {
        short len = ReadShort();
        if (len < 0 || len > 240) throw new IOException($"bad string length {len}");
        var bytes = ReadExact(len * 2);
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    public byte[] ReadBytes(int count) => ReadExact(count);

    // TCP hands you SOME bytes, never "the bytes you asked for" - always loop.
    private byte[] ReadExact(int count)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = mStream.Read(buffer, read, count - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }

        return buffer;
    }
    
    /// Consumes an entity-metadata stream, whose only job here is to be got past: MobSpawn carries
    /// one and nothing on this end reads it yet. Same key encoding as PacketLayout.Metadata.
    public void ReadMetadata()
    {
        while (true)
        {
            byte key = ReadByte();
            if (key == 0x7F) return;

            switch (key >> 5)
            {
                case 0: ReadByte(); break;
                case 1: ReadShort(); break;
                case 2: ReadInt(); break;
                case 3: ReadFloat(); break;
                case 4: ReadString(); break;
                case 5: ReadItem(); break;
                case 6: ReadInt(); ReadInt(); ReadInt(); break;
                default: throw new IOException($"bad metadata type in key 0x{key:X2}");
            }
        }
    }

    // --- Writing ---
    public void WriteByte(byte v) => mStream.WriteByte(v);
    public void WriteSByte(sbyte v) => mStream.WriteByte((byte)v);
    public void WriteBool(bool v) => mStream.WriteByte(v ? (byte)1 : (byte)0);

    public void WriteShort(short v)
    {
        WriteByte((byte)(v >>8));
        WriteByte((byte)v);
    }

    public void WriteInt(int v)
    {
        WriteByte((byte)(v >> 24));
        WriteByte((byte)(v >> 16));
        
        WriteByte((byte)(v >> 8));
        WriteByte((byte)v);
    }

    public void WriteLong(long v)
    {
        for (int i = 7; i >= 0; i--)
        {
            WriteByte((byte)(v >> (i * 8)));
        }
    }

    public void WriteFloat(float v)
    {
        var b = BitConverter.GetBytes(v);
        Array.Reverse(b);
        mStream.Write(b);
    }

    public void WriteDouble(double v)
    {
        var b = BitConverter.GetBytes(v);
        Array.Reverse(b);
        mStream.Write(b);
    }

    public void WriteString(string s)
    {
        // ReadString rejects >240, and with no length prefix to skip past, an unreadable string
        // kills the connection rather than losing one message. Fail on the sending side instead.
        if (s.Length > 240)
            throw new IOException($"string too long to send: {s.Length} chars (max 240)");

        WriteShort((short)s.Length);
        mStream.Write(Encoding.BigEndianUnicode.GetBytes(s));
    }

    public void WriteBytes(byte[] b) => mStream.Write(b);
    public void Flush() => mStream.Flush();

    // --- Packet level convenice ---
    public PacketId ReadPacketId() => (PacketId)ReadByte();
    public void WritePacketId(PacketId id) => WriteByte((byte)id);

    // Entity coords go as fixed-point 1/32 blocks
    public void WriteFixedPos(Vector3 p)
    {
        // Parens matter: (int)p.X * 32 truncates to a whole block before scaling, throwing away the
        // sub-block precision this format exists to carry.
        WriteInt((int)(p.X * 32));
        WriteInt((int)(p.Y * 32));
        WriteInt((int)(p.Z * 32));
    }

    // A method, not a property - it consumes 12 bytes, so a watch window evaluating it would eat
    // part of the connection.
    public Vector3 ReadFixedPos() => new(ReadInt() / 32f, ReadInt() / 32f, ReadInt() / 32f);

    // Beta's "slot": short id, and ONLY if it's >= 0, a count byte and damage short. An empty slot
    // is just the -1. This must match PacketLayout's Item() exactly or every following field shifts.
    //
    // Blocks occupy 0-255 (BlockType tops out at 255) and items start above that, so one short
    // carries either. Both ends only have to agree with each other.
    private const int ITEM_ID_BASE = 256;

    public void WriteItem(ItemStack? stack)
    {
        if (stack is not { } s)
        {
            WriteShort(-1);
            return;
        }

        WriteShort(s.IsBlock ? (short)s.Block : (short)(ITEM_ID_BASE + (int)s.Item));
        WriteByte((byte)Math.Clamp(s.Count, 0, 255));
        WriteShort((short)Math.Max(s.Durability, 0));
    }

    public ItemStack? ReadItem()
    {
        short id = ReadShort();
        if (id < 0)
            return null;

        int count = ReadByte();
        short damage = ReadShort();

        return id < ITEM_ID_BASE
            ? ItemStack.FromBlock((BlockType)id, count)
            : ItemStack.FromItem((ItemType)(id - ITEM_ID_BASE), count).WithDurability(damage);
    }

    public void WriteAngle(float degrees) => WriteByte((byte)(int)(degrees * 256f / 360f));
    public float ReadAngle() => ReadByte() * 360f / 256f;
}