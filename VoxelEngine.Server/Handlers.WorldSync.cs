using VoxelEngine.Core;
using VoxelEngine.Net;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    private const int MULTI_BLOCK_THRESHOLD = 2;

    private void FlushBlockChanges()
    {
        foreach (var chunkGroup in mWorld.DrainChanges())
        {
            var coord = chunkGroup.Key;
            var changes = chunkGroup.ToList();

            var viewers = mPlayers.Where(p => p.SentChunks.Contains(coord)).ToList();

            if (viewers.Count == 0)
                continue;

            if (changes.Count < MULTI_BLOCK_THRESHOLD)
            {
                var (pos, value) = (changes[0].Key, changes[0].Value);
                foreach (var v in viewers)
                {
                    SendTo(v, PacketId.BlockChange, w =>
                    {
                        w.WriteInt(pos.X);
                        w.WriteByte((byte)pos.Y);
                        w.WriteInt(pos.Z);

                        w.WriteByte((byte)value.type);
                        w.WriteByte(value.metadata);
                    });
                }

                continue;
            }

            foreach (var v in viewers)
            {
                SendTo(v, PacketId.MultiBlockChange, w =>
                {
                    w.WriteInt(coord.X);
                    w.WriteInt(coord.Z);
                    w.WriteShort((short)changes.Count);

                    foreach (var c in changes)
                    {
                        int lx = c.Key.X & 15, lz = c.Key.Z & 15;
                        w.WriteShort((short)((lx << 12) | (lz << 8) | (c.Key.Y & 0xFF)));
                    }

                    foreach (var c in changes) w.WriteByte((byte)c.Value.type);
                    foreach (var c in changes) w.WriteByte(c.Value.metadata);
                });
            }
        }
    }

    private void BroadcastTime()
    {
        if (mTickCount % (TickSystem.TPS * 5) != 0)
            return;

        Broadcast(PacketId.TimeUpdate, w => w.WriteLong(mWorldTime));
    }

    internal void BroadcastEffect(Vector3i pos, EffectId effect, int data)
    {
        foreach (var viewer in ViewersOf(pos))
        {
            SendTo(viewer, PacketId.SoundEffect, w =>
            {
                w.WriteInt((int)effect);
                w.WriteInt(pos.X);
                w.WriteByte((byte)pos.Y);
                w.WriteInt(pos.Z);
                w.WriteInt(data); // block type for break particles, etc.
            });
        }
    }
    
    private void CheckKeepAliveTimeouts()
    {
        long now = Environment.TickCount64;
        foreach (var p in mPlayers.ToList())
        {
            if (now - p.LastKeepAlive > 30_000) 
                p.Connection.Kick("Timed out");
        }
    }
}
