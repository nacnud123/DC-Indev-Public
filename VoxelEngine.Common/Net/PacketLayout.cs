namespace VoxelEngine.Net;

/// Consumes exactly one packet's fields from the stream and buffers them, so the handler can re-read
/// them at its leisure. Beta has no length prefix, so the read loop cannot "skip to the next packet"
/// - it has to parse. One wrong field here desynchronises the connection permanently.
public static class PacketLayout
{
    public static byte[] ReadBody(PacketId id, NetStream s)
    {
        var buffer = new MemoryStream();
        var w = new NetStream(buffer);

        switch (id)
        {
            case PacketId.KeepAlive: break; // no payload at all

            case PacketId.LoginRequest:
                Int(s, w);
                Str(s, w);
                Long(s, w);
                Byte(s, w);
                break;
            case PacketId.Handshake: Str(s, w); break;
            case PacketId.ChatMessage: Str(s, w); break;
            case PacketId.TimeUpdate: Long(s, w); break;
            case PacketId.EntityEquipment:
                Int(s, w);
                Short(s, w);
                Short(s, w);
                break;
            case PacketId.SpawnPosition:
                Int(s, w);
                Int(s, w);
                Int(s, w);
                break;
            case PacketId.UseEntity:
                Int(s, w);
                Int(s, w);
                Byte(s, w);
                break;
            case PacketId.UpdateHealth: Short(s, w); break;
            case PacketId.Respawn: Byte(s, w); break;

            case PacketId.Player: Byte(s, w); break; // onGround
            case PacketId.PlayerPosition:
                Dbl(s, w);
                Dbl(s, w);
                Dbl(s, w);
                Dbl(s, w);
                Byte(s, w);
                break;
            case PacketId.PlayerLook:
                Flt(s, w);
                Flt(s, w);
                Byte(s, w);
                break;
            case PacketId.PlayerPositionLook:
                Dbl(s, w);
                Dbl(s, w);
                Dbl(s, w);
                Dbl(s, w);
                Flt(s, w);
                Flt(s, w);
                Byte(s, w);
                break;

            case PacketId.PlayerDigging:
                Byte(s, w);
                Int(s, w);
                Byte(s, w);
                Int(s, w);
                Byte(s, w);
                break;
            case PacketId.PlayerBlockPlacement:
                Int(s, w);
                Byte(s, w);
                Int(s, w);
                Byte(s, w);
                Item(s, w);
                break;
            case PacketId.HoldingChange: Short(s, w); break;
            case PacketId.Animation:
                Int(s, w);
                Byte(s, w);
                break;

            case PacketId.NamedEntitySpawn:
                Int(s, w);
                Str(s, w);
                Int(s, w);
                Int(s, w);
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                Short(s, w);
                break;

            case PacketId.PickupSpawn:
                Int(s, w);
                Short(s, w);
                Byte(s, w);
                Short(s, w);
                Int(s, w);
                Int(s, w);
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                Byte(s, w);
                break;

            case PacketId.CollectItem:
                Int(s, w);
                Int(s, w);
                break;

            case PacketId.AddObject:
            {
                Int(s, w);
                Byte(s, w);
                Int(s, w);
                Int(s, w);
                Int(s, w);
                // Trailing "thrower id" - and if it's non-zero, three more shorts follow. This
                // conditional tail is exactly the kind of thing that desyncs a naive reader.
                int thrower = CopyIntReturn(s, w);
                if (thrower > 0)
                {
                    Short(s, w);
                    Short(s, w);
                    Short(s, w);
                }

                break;
            }

            case PacketId.MobSpawn:
                Int(s, w);
                Byte(s, w);
                Int(s, w);
                Int(s, w);
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                Metadata(s, w); // 0x7F-terminated
                break;

            case PacketId.EntityPainting:
                Int(s, w);
                Str(s, w);
                Int(s, w);
                Int(s, w);
                Int(s, w);
                Int(s, w);
                break;

            case PacketId.EntityVelocity:
                Int(s, w);
                Short(s, w);
                Short(s, w);
                Short(s, w);
                break;
            case PacketId.DestroyEntity: Int(s, w); break;
            case PacketId.Entity: Int(s, w); break;
            case PacketId.EntityRelativeMove:
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                Byte(s, w);
                break;
            case PacketId.EntityLook:
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                break;
            case PacketId.EntityLookRelMove:
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                Byte(s, w);
                Byte(s, w);
                Byte(s, w);
                break;
            case PacketId.EntityTeleport:
                Int(s, w);
                Int(s, w);
                Int(s, w);
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                break;

            case PacketId.PreChunk:
                Int(s, w);
                Int(s, w);
                Byte(s, w);
                break;

            case PacketId.MapChunk:
                Int(s, w);
                Short(s, w);
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                Byte(s, w);
                Blob(s, w); // int length + bytes
                break;

            case PacketId.MultiBlockChange:
            {
                Int(s, w);
                Int(s, w);
                short count = CopyShortReturn(s, w);
                // Three parallel arrays: packed coords, then types, then metadata.
                Bytes(s, w, count * 2);
                Bytes(s, w, count);
                Bytes(s, w, count);
                break;
            }

            case PacketId.BlockChange:
                Int(s, w);
                Byte(s, w);
                Int(s, w);
                Byte(s, w);
                Byte(s, w);
                break;

            case PacketId.Explosion:
            {
                Dbl(s, w);
                Dbl(s, w);
                Dbl(s, w);
                Flt(s, w);
                int records = CopyIntReturn(s, w);
                Bytes(s, w, records * 3); // 3 sbytes per record
                break;
            }

            case PacketId.SoundEffect:
                Int(s, w);
                Int(s, w);
                Byte(s, w);
                Int(s, w);
                Int(s, w);
                break;

            case PacketId.OpenWindow:
                Byte(s, w);
                Byte(s, w);
                Str(s, w);
                Byte(s, w);
                break;
            case PacketId.CloseWindow: Byte(s, w); break;
            case PacketId.WindowClick:
                Byte(s, w);
                Short(s, w);
                Byte(s, w);
                Short(s, w);
                Byte(s, w);
                Item(s, w);
                break;
            case PacketId.SetSlot:
                Byte(s, w);
                Short(s, w);
                Item(s, w);
                break;

            case PacketId.WindowItems:
            {
                Byte(s, w);
                short count = CopyShortReturn(s, w);
                for (int i = 0; i < count; i++) Item(s, w); // variable-size each
                break;
            }

            case PacketId.UpdateProgressBar:
                Byte(s, w);
                Short(s, w);
                Short(s, w);
                break;
            case PacketId.Transaction:
                Byte(s, w);
                Short(s, w);
                Byte(s, w);
                break;

            case PacketId.UpdateSign:
                Int(s, w);
                Short(s, w);
                Int(s, w);
                Str(s, w);
                Str(s, w);
                Str(s, w);
                Str(s, w);
                break;

            case PacketId.PlayerSkin:
                Int(s, w);
                Blob(s, w);
                break; // our extension
            case PacketId.DisconnectKick: Str(s, w); break;

            default:
                // An unknown id means the stream is already desynchronised - there is no safe way to
                // resynchronise without a length prefix, so fail loudly instead of guessing.
                throw new IOException($"Unknown packet id 0x{(byte)id:X2}; stream desynchronised");
        }

        w.Flush();
        return buffer.ToArray();
    }

    // --- Field Copiers: Read from the socket ---
    private static void Byte(NetStream s, NetStream w) => w.WriteByte(s.ReadByte());
    private static void Short(NetStream s, NetStream w) => w.WriteShort(s.ReadShort());
    private static void Int(NetStream s, NetStream w) => w.WriteInt(s.ReadInt());
    private static void Long(NetStream s, NetStream w) => w.WriteLong(s.ReadLong());
    private static void Flt(NetStream s, NetStream w) => w.WriteFloat(s.ReadFloat());
    private static void Dbl(NetStream s, NetStream w) => w.WriteDouble(s.ReadDouble());
    private static void Str(NetStream s, NetStream w) => w.WriteString(s.ReadString());

    private static int CopyIntReturn(NetStream s, NetStream w)
    {
        int v = s.ReadInt();
        w.WriteInt(v);
        return v;
    }

    private static short CopyShortReturn(NetStream s, NetStream w)
    {
        short v = s.ReadShort();
        w.WriteShort(v);
        return v;
    }

    private static void Bytes(NetStream s, NetStream w, int count)
    {
        if (count < 0 || count > 1_000_000) throw new IOException($"absurd array length {count}");
        w.WriteBytes(s.ReadBytes(count));
    }

    private static void Blob(NetStream s, NetStream w)
    {
        int length = CopyIntReturn(s, w);
        Bytes(s, w, length);
    }

    /// Beta's "slot" composite: short item id, and ONLY if it's >= 0, a count byte and damage short. An empty slot is just the -1. Getting this wrong shifts every following field.
    private static void Item(NetStream s, NetStream w)
    {
        short id = CopyShortReturn(s, w);
        if (id < 0) return;
        Byte(s, w); // stack count
        Short(s, w); // damage / metadata
    }

    /// Entity metadata stream: [byte key][typed value]... terminated by 0x7F. The top 3 bits of the key select the value type. Only MobSpawn and EntityMetadata use it.
    private static void Metadata(NetStream s, NetStream w)
    {
        while (true)
        {
            byte key = s.ReadByte();
            w.WriteByte(key);
            if (key == 0x7F) return;

            switch (key >> 5)
            {
                case 0: Byte(s, w); break;
                case 1: Short(s, w); break;
                case 2: Int(s, w); break;
                case 3: Flt(s, w); break;
                case 4: Str(s, w); break;
                case 5: Item(s, w); break;
                case 6:
                    Int(s, w);
                    Int(s, w);
                    Int(s, w);
                    break;
                default: throw new IOException($"bad metadata type in key 0x{key:X2}");
            }
        }
    }
}