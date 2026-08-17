using System.Collections.Concurrent;
using VoxelEngine.Saving;

namespace VoxelEngine.Terrain.Infinite;

public sealed class GeneratingChunkSource : IChunkSource
{
    private readonly World mWorld;
    private readonly InfiniteTerrainGenerator mGenerator;
    private readonly string mSaveDir;
    private readonly ConcurrentDictionary<ChunkCoord, byte> mInFlight = new();
    private readonly ConcurrentQueue<Chunk> mCompleted = new();

    public GeneratingChunkSource(World world, InfiniteTerrainGenerator generator, string saveDir)
    {
        mWorld = world;
        mGenerator = generator;
        mSaveDir = saveDir;
    }

    public void RequestChunk(ChunkCoord coord)
    {
        if(!mInFlight.TryAdd(coord, 0))
            return;

        Task.Run(() =>
        {
            try
            {
                // Worker Thread. Touches only this chunk's array. Plus pure noise. No world, no gl, no shared collections
                var chunk = new Chunk(coord.X, coord.Z, mWorld);
                if (!ChunkBlob.TryLoadFromDisk(chunk, mSaveDir))
                {
                    mGenerator.Generate(chunk, coord.X, coord.Z);
                }

                // Two passes over 32,768 voxels. Doing it here instead of at integration time is
                // most of the difference between a smooth stream-in and a hitch per chunk.
                mWorld.SeedChunkLightingOffThread(chunk);

                mCompleted.Enqueue(chunk);
            }
            catch (Exception ex)
            {
                // Nothing awaits this Task, so an escaping exception would vanish silently and the
                // chunk would just never appear - a hole in the world with no clue why.
                Console.WriteLine($"[ChunkSource] Generating {coord} failed: {ex}");
            }
            finally
            {
                // Cleared only once the chunk is queued, not once it's integrated. A duplicate
                // request in that window is harmless: IntegrateReady drops any coord already
                // resident, so the worst case is one wasted generation.
                mInFlight.TryRemove(coord, out _);
            }
        });
    }

    public bool TryDequeueCompleted(out Chunk chunk) => mCompleted.TryDequeue(out chunk!);
}