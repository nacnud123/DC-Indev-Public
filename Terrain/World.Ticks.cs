// A partial class that extends World that has functions related to random ticks and scheduled ticks. | DA | 2/14/26
using OpenTK.Mathematics;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public partial class World
{
    private const int RANDOM_DISPLAY_RADIUS = 16;
    private const int RANDOM_DISPLAY_ITERATIONS = 1000;
    private const int RANDOM_TICKS_PER_CHUNK = 24;
    private const int MAX_BLOCK_TICKS_PER_TICK = 256;

    public void RandomDisplayUpdates(Vector3 playerPos)
    {
        int px = (int)playerPos.X;
        int py = (int)playerPos.Y;
        int pz = (int)playerPos.Z;

        for (int i = 0; i < RANDOM_DISPLAY_ITERATIONS; i++)
        {
            int x = px + mWorldRand.Next(-RANDOM_DISPLAY_RADIUS, RANDOM_DISPLAY_RADIUS + 1);
            int y = py + mWorldRand.Next(-RANDOM_DISPLAY_RADIUS, RANDOM_DISPLAY_RADIUS + 1);
            int z = pz + mWorldRand.Next(-RANDOM_DISPLAY_RADIUS, RANDOM_DISPLAY_RADIUS + 1);

            var blockType = GetBlock(x, y, z);
            if (blockType != BlockType.Air)
            {
                BlockRegistry.Get(blockType).RandomDisplayTick(x, y, z, mWorldRand);
            }
        }
    }

    public void ScheduleBlockTick(int x, int y, int z)
    {
        var blockType = GetBlock(x, y, z);
        int tickRate = BlockRegistry.GetTickRate(blockType);
        if (tickRate <= 0)
            return;

        var key = (x, y, z);
        if (mScheduledTickSet.Contains(key))
            return;

        mScheduledTickSet.Add(key);
        mBlockTickQueue.Enqueue((x, y, z, tickRate));
    }

    private void ScheduleNeighborTicks(int x, int y, int z)
    {
        ScheduleBlockTick(x, y + 1, z);
        ScheduleBlockTick(x, y - 1, z);
        ScheduleBlockTick(x + 1, y, z);
        ScheduleBlockTick(x - 1, y, z);
        ScheduleBlockTick(x, y, z + 1);
        ScheduleBlockTick(x, y, z - 1);
    }

    public void DoScheduledTick()
    {
        int count = Math.Min(mBlockTickQueue.Count, MAX_BLOCK_TICKS_PER_TICK);
        for (int i = 0; i < count; i++)
        {
            var (x, y, z, countdown) = mBlockTickQueue.Dequeue();

            if (countdown > 0)
            {
                mBlockTickQueue.Enqueue((x, y, z, countdown - 1));
                continue;
            }

            // countdown == 0: fire the tick
            mScheduledTickSet.Remove((x, y, z));

            int chunkX = x >= 0 ? x / Chunk.WIDTH : (x + 1) / Chunk.WIDTH - 1;
            int chunkZ = z >= 0 ? z / Chunk.DEPTH : (z + 1) / Chunk.DEPTH - 1;
            if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
                continue;
            if (!mChunks[chunkX, chunkZ].IsLoaded)
                continue;

            var blockType = GetBlock(x, y, z);
            var blockDef = BlockRegistry.Get(blockType);
            if (blockDef.TickRate > 0)
                blockDef.ScheduledTick(this, x, y, z, mWorldRand);
        }
    }

    public void DoRandomTick()
    {
        for (int cx = 0; cx < SizeInChunks; cx++)
        {
            for (int cz = 0; cz < SizeInChunks; cz++)
            {
                var chunk = mChunks[cx, cz];

                if (!chunk.IsLoaded)
                    continue;

                for (int i = 0; i < RANDOM_TICKS_PER_CHUNK; i++)
                {
                    mRandomTickSeed = mRandomTickSeed * 1664525 + 1013904223;

                    int rX = (mRandomTickSeed >> 4) & (Chunk.WIDTH - 1);
                    int rZ = (mRandomTickSeed >> 8) & (Chunk.DEPTH - 1);
                    int rY = (mRandomTickSeed >> 12) & (Chunk.HEIGHT - 1);

                    BlockType blockType = chunk.GetBlock(rX, rY, rZ);

                    if (blockType == BlockType.Air || !BlockRegistry.TicksRandomly(blockType))
                        continue;

                    int worldX = cx * Chunk.WIDTH + rX;
                    int worldZ = cz * Chunk.DEPTH + rZ;
                    BlockRegistry.Get(blockType).RandomTick(this, worldX, rY, worldZ, mWorldRand);
                }
            }
        }
    }
}
