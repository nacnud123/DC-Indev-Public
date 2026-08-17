// A partial class that extends World that has functions related to random ticks and scheduled ticks. | DA | 2/14/26

using VoxelEngine.Rendering;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public partial class World
{
    // How far (in blocks) around the player RandomDisplayUpdates samples positions for purely
    // cosmetic effects (e.g. smoke/ambient particles), and how many random samples to take per call.
    private const int RANDOM_DISPLAY_RADIUS = 16;
    private const int RANDOM_DISPLAY_ITERATIONS = 1000;
    // How many random block positions get sampled per loaded chunk each game tick for gameplay
    // random ticks (crop growth, leaf decay, etc.) - see DoRandomTick.
    private const int RANDOM_TICKS_PER_CHUNK = 24;
    // Upper bound on how many scheduled block ticks are processed in a single DoScheduledTick
    // call, so a huge backlog (e.g. after a big fluid spill) doesn't stall a single game tick.
    private const int MAX_BLOCK_TICKS_PER_TICK = 256;

    // Purely visual: picks random nearby positions and asks whatever block is there to spawn a
    // display effect (e.g. smoke above lava, fire particles). Does not affect simulation state -
    // this is separate from DoRandomTick, which does actual gameplay logic.
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

    // Queues a delayed tick for the block at (x,y,z), to fire after tickRate game ticks (e.g.
    // used for fluid flow updates). No-ops if the block type doesn't tick (tickRate <= 0) or if a
    // tick is already pending for this exact position (tracked via mScheduledTickSet) - this
    // matters because SetBlock schedules ticks for both the changed block and all its neighbors,
    // and without de-duplication the same position could pile up multiple redundant tick entries.
    public void ScheduleBlockTick(int x, int y, int z)
    {
        // A networked client never drains this queue - the server runs the simulation and sends the
        // results - so scheduling here would grow the queue and its dedupe set for the whole session.
        if (ServerDrivenBlockTicks)
            return;

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

    // Schedules a tick for each of the 6 face-adjacent neighbors of (x,y,z), used after a block
    // changes so surrounding blocks (fluids especially) get a chance to react.
    private void ScheduleNeighborTicks(int x, int y, int z)
    {
        ScheduleBlockTick(x, y + 1, z);
        ScheduleBlockTick(x, y - 1, z);
        ScheduleBlockTick(x + 1, y, z);
        ScheduleBlockTick(x - 1, y, z);
        ScheduleBlockTick(x, y, z + 1);
        ScheduleBlockTick(x, y, z - 1);
    }

    // Processes the scheduled-tick queue, up to MAX_BLOCK_TICKS_PER_TICK entries per call. Only
    // "count" entries (the queue's size at the start of this call) are processed even though
    // entries with countdown > 0 get re-enqueued at the back - this bounds the work to one pass
    // over what was in the queue at the start, rather than looping forever re-processing
    // just-re-added entries.
    public void DoScheduledTick()
    {
        int count = Math.Min(mBlockTickQueue.Count, MAX_BLOCK_TICKS_PER_TICK);
        for (int i = 0; i < count; i++)
        {
            var (x, y, z, countdown) = mBlockTickQueue.Dequeue();

            if (countdown > 0)
            {
                // Not ready yet - decrement and put it back at the end of the queue.
                mBlockTickQueue.Enqueue((x, y, z, countdown - 1));
                continue;
            }

            // countdown == 0: fire the tick
            mScheduledTickSet.Remove((x, y, z));

            // Skip if the chunk has streamed out, or is resident but outside simulation range.
            // Either way the entry is already off mScheduledTickSet, so it's simply dropped.
            var chunk = GetChunk(x >> 4, z >> 4);
            if (chunk == null || !chunk.IsLoaded)
                continue;

            var blockType = GetBlock(x, y, z);
            var blockDef = BlockRegistry.Get(blockType);
            if (blockDef.TickRate > 0)
                blockDef.ScheduledTick(this, x, y, z, mWorldRand);
        }
    }

    // Simulates "random ticks" (Minecraft-style ambient block updates: crop/sapling growth, leaf
    // decay, etc.) by sampling RANDOM_TICKS_PER_CHUNK pseudo-random positions per loaded chunk
    // each game tick, rather than updating every block every tick (which would be far too slow).
    public void DoRandomTick()
    {
        foreach (var chunk in LoadedChunks)
        {
            if (!chunk.IsLoaded)
                continue;

            for (int i = 0; i < RANDOM_TICKS_PER_CHUNK; i++)
            {
                // Classic LCG (linear congruential generator) step using the same constants
                // Minecraft uses - cheap, deterministic, and good enough for picking scatter
                // positions (not used for anything requiring real randomness/security).
                mRandomTickSeed = mRandomTickSeed * 1664525 + 1013904223;

                // Extract different bit ranges of the LCG state for each axis so X/Y/Z don't
                // end up correlated. "& (WIDTH-1)" etc. masks down to a value in [0, size)
                // since WIDTH/DEPTH/HEIGHT are all powers of two.
                int rX = (mRandomTickSeed >> 4) & (Chunk.WIDTH - 1);
                int rZ = (mRandomTickSeed >> 8) & (Chunk.DEPTH - 1);
                int rY = (mRandomTickSeed >> 12) & (Chunk.HEIGHT - 1);

                BlockType blockType = chunk.GetBlock(rX, rY, rZ);

                if (blockType == BlockType.Air || !BlockRegistry.TicksRandomly(blockType))
                    continue;

                // Chunk coords come off the chunk itself now, not the loop counters - with negative
                // chunk coordinates in play these are no longer the same thing.
                int worldX = chunk.ChunkX * Chunk.WIDTH + rX;
                int worldZ = chunk.ChunkZ * Chunk.DEPTH + rZ;
                BlockRegistry.Get(blockType).RandomTick(this, worldX, rY, worldZ, mWorldRand);
            }
        }
    }
}
