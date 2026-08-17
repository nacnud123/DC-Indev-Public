using VoxelEngine.Net;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Infinite;

namespace VoxelEngine.Server;

public partial class DuncanCraftServer
{
    private const int MAX_CHUNKS_PER_TICK = 2;

    private void SendChunks(ServerPlayer player)
    {
        var wanted = ChunkMath.Within(player.Position, player.ViewDistanceChunks).ToHashSet();

        foreach (var coord in wanted.Except(player.SentChunks)
                     .OrderBy(c => ChunkMath.Distance(c, player.Position))
                     .Take(MAX_CHUNKS_PER_TICK))
        {
            var chunk = mWorld.Streamer.GetChunk(coord.X, coord.Z);
            if (chunk == null)
                continue;

            SendTo(player, PacketId.PreChunk, w =>
            {
                w.WriteInt(coord.X);
                w.WriteInt(coord.Z);
                w.WriteBool(true);
            });

            var blob = ChunkBlob.ToWireBytes(chunk);

            SendTo(player, PacketId.MapChunk, w =>
            {
                w.WriteInt(coord.X * Chunk.WIDTH);
                w.WriteShort(0);
                w.WriteInt(coord.Z * Chunk.DEPTH);
                w.WriteByte(Chunk.WIDTH - 1);
                w.WriteByte(Chunk.HEIGHT - 1);
                w.WriteByte(Chunk.DEPTH - 1);
                w.WriteInt(blob.Length);
                w.WriteBytes(blob);
            });

            player.SentChunks.Add(coord);
        }

        foreach (var coord in player.SentChunks
                     .Where(c => ChunkMath.Distance(c, player.Position) > player.ViewDistanceChunks + 2).ToList())
        {
            SendTo(player, PacketId.PreChunk, w =>
            {
                w.WriteInt(coord.X);
                w.WriteInt(coord.Z);
                w.WriteBool(false);
            });

            player.SentChunks.Remove(coord);
        }
    }
}