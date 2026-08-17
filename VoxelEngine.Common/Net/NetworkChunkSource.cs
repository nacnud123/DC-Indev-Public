using System.Collections.Concurrent;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Infinite;

namespace VoxelEngine.Net;

public sealed class NetworkChunkSource : IChunkSource
{
    private readonly World mWorld;
    private readonly ConcurrentQueue<Chunk> mCompleted = new();

    public NetworkChunkSource(World world) => mWorld = world;

    // The server's world, not ours - see IChunkSource.PersistsToDisk.
    public bool PersistsToDisk => false;

    public void RequestChunk(ChunkCoord coord)
    {
        
    }

    public bool TryDequeueCompleted(out Chunk chunk) => mCompleted.TryDequeue(out chunk!);

    public void OnMapChunk(int chunkX, int chunkZ, byte[] blob)
    {
        var chunk = new Chunk(chunkX, chunkZ, mWorld);
        ChunkBlob.FromWireBytes(chunk, blob);
        mCompleted.Enqueue(chunk);
    }

    public void OnChunkUnload(int chunkX, int chunkZ) => mWorld.Streamer.ForceUnload(new ChunkCoord(chunkX, chunkZ));
}