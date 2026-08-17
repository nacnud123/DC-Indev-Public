// Minecraft-style light propagation using BFS flood-fill. Processes both sunlight and block light emission with incremental updates. | DA | 2/5/26
using VoxelEngine.Core;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

/// <summary>
/// Owns all sky-light and block-light propagation for the world using a Minecraft-style
/// BFS flood-fill (four independent queues: add/remove for sky light, add/remove for block light).
/// Light values are per-voxel bytes in [0, Chunk.MAX_LIGHT]. Propagation falls off by 1 per block step,
/// further reduced by the destination block's <see cref="BlockRegistry.GetBlockOpacity"/>, and stops once
/// the propagated value is no longer brighter than what's already stored (standard flood-fill early-out).
/// Updates are queued (rather than applied instantly) and drained a bounded number of nodes per tick via
/// <see cref="ProcessTick"/> to keep frame time bounded when many blocks change at once (e.g. explosions).
/// </summary>
public class LightingEngine
{
    // Upper bound on light-graph nodes processed in a single ProcessTick call, so a large
    // cascade of light changes (e.g. TNT blowing a hole to the sky) is spread over several ticks
    // instead of stalling the game loop.
    private const int MAX_UPDATES_PER_TICK = 2048;

    private readonly World mWorld;

    // "Add" queues hold positions whose light increased and may need to spread to neighbors.
    // "Removal" queues hold positions whose light source was removed/blocked and whose darkness
    // needs to spread to any neighbors that were lit *because of* that now-gone light.
    private readonly Queue<LightNode> mSkyLightQueue = new();
    private readonly Queue<LightRemovalNode> mSkyRemovalQueue = new();
    private readonly Queue<LightNode> mBlockLightQueue = new();
    private readonly Queue<LightRemovalNode> mBlockRemovalQueue = new();

    /// <summary>A world-space voxel coordinate pending light propagation (light increase).</summary>
    private struct LightNode
    {
        public int X, Y, Z;

        public LightNode(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// A world-space voxel coordinate pending light removal, carrying the light level it had
    /// *before* being zeroed out. Neighbors dimmer than <see cref="OldLight"/> are assumed to have
    /// been lit by this node and are darkened in turn (BFS "unlight" pass); neighbors at or above
    /// this level are independently lit and are instead re-enqueued to re-propagate into the gap.
    /// </summary>
    private struct LightRemovalNode
    {
        public int X, Y, Z;
        public byte OldLight;

        public LightRemovalNode(int x, int y, int z, byte oldLight)
        {
            X = x;
            Y = y;
            Z = z;
            OldLight = oldLight;
        }
    }

    // Configured min/max clamp bounds for block-emitted light, sourced from WorldGenSettings
    // and capped at the engine-wide Chunk.MAX_LIGHT ceiling.
    private readonly int mMinBlockLight;
    private readonly int mMaxBlockLight;


    // Configured min/max clamp bounds for sky (sun) light, same sourcing as above.
    private readonly int mMinSunLight;
    private readonly int mMaxSunLight;

    private int ClampBlockLight(int light) => Math.Clamp(light, mMinBlockLight, mMaxBlockLight);
    private int ClampSunLight(int light) => Math.Clamp(light, mMinSunLight, mMaxSunLight);

    /// <summary>Binds this engine to the owning world and reads light clamp bounds from world-gen settings.</summary>
    public LightingEngine(World world)
    {
        mWorld = world;
        var settings = GameContext.Current.GetWorldGenSettings;

        mMinBlockLight = settings.MinBlockLightLevel;
        mMaxBlockLight = Math.Min(settings.MaxBlockLightLevel, Chunk.MAX_LIGHT);

        // Initialize sunlight clamp values so ClampSunLight works
        mMinSunLight = settings.MinSunLightLevel;
        mMaxSunLight = Math.Min(settings.MaxSunLightLevel, Chunk.MAX_LIGHT);
    }

    /// <summary>
    /// Outstanding sky-light BFS nodes. Diagnostic: this is the number that tells you whether
    /// seeding is out-running the 1024-per-tick drain rate. If it climbs without bound, caves stop
    /// lighting up and no amount of waiting fixes it.
    /// </summary>
    public int PendingSkyLightNodes => mSkyLightQueue.Count;

    /// <summary>True while any of the four propagation/removal queues still has work outstanding.</summary>
    public bool HasPendingUpdates =>
        mSkyLightQueue.Count > 0 || mSkyRemovalQueue.Count > 0 ||
        mBlockLightQueue.Count > 0 || mBlockRemovalQueue.Count > 0;

    /// <summary>
    /// Runs once when a chunk is first generated. Computes the initial per-column sky light by
    /// walking straight down from Chunk.MAX_LIGHT at the top of the world, subtracting each block's
    /// opacity as it descends (so light dims through leaves/water and stops dead at fully opaque
    /// blocks) — this is a column scan, not a BFS, since direct-from-sky light only ever travels
    /// straight down at generation time. Separately scans every block for light emitters (torches,
    /// glowstone, lava, etc.) and seeds them into the block-light BFS queue for a later full propagate pass.
    /// </summary>
    public void CalculateInitialLighting(Chunk chunk)
    {
        // Already done on a generation worker? Then all that's left is the queue seeding, which
        // must happen on the main thread.
        if (chunk.PendingLightEmitters != null || chunk.PendingSkyLightSeeds != null)
        {
            CommitPendingEmitters(chunk);
            return;
        }

        SeedInitialLightingOffThread(chunk);
        CommitPendingEmitters(chunk);
    }

    /// <summary>
    /// The expensive part of <see cref="CalculateInitialLighting"/> - two passes over all 32,768
    /// voxels - with the shared-queue writes stripped out, so a generation worker can run it.
    /// Only the chunk's own light arrays are touched. Emitters found are parked on the chunk for
    /// <see cref="CommitPendingEmitters"/> to enqueue once it reaches the main thread.
    /// </summary>
    public void SeedInitialLightingOffThread(Chunk chunk)
    {
        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int light = Chunk.MAX_LIGHT;
                for (int y = Chunk.HEIGHT - 1; y >= 0; y--)
                {
                    int opacity = BlockRegistry.GetBlockOpacity(chunk.GetBlock(x, y, z));
                    light = Math.Max(0, light - opacity);

                    light = ClampSunLight(light);

                    chunk.SetSkyLightDirect(x, y, z, (byte)light);
                }
            }
        }

        var emitters = new List<Vector3i>();

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                for (int y = 0; y < Chunk.HEIGHT; y++)
                {
                    var block = chunk.GetBlock(x, y, z);
                    if (block == BlockType.Air)
                        continue;

                    int emission = BlockRegistry.Get(block).LightEmission;
                    if (emission <= 0)
                        continue;

                    chunk.SetBlockLightDirect(x, y, z, (byte)Math.Min(emission, mMaxBlockLight));
                    emitters.Add(new Vector3i(x, y, z));
                }
            }
        }

        chunk.PendingLightEmitters = emitters;
        chunk.PendingSkyLightSeeds = CollectSkyLightFrontier(chunk);
    }

    /// <summary>
    /// Finds where the column scan's result has to start spreading sideways, so caves under an
    /// overhang aren't left pitch black.
    ///
    /// The test is exactly <see cref="TryPropagateSky"/>'s: seed a voxel only if it would actually
    /// raise a neighbour's light. Anything looser is unaffordable - the sky BFS drains 1024 nodes a
    /// tick, so seeding (say) every lit voxel next to solid rock queues work faster than it can be
    /// consumed and the backlog never clears.
    ///
    /// Chunk edges are skipped here; <see cref="SeedAcrossBorder"/> handles those once both sides
    /// exist and their real light values can be compared.
    /// </summary>
    private List<Vector3i> CollectSkyLightFrontier(Chunk chunk)
    {
        var seeds = new List<Vector3i>();

        for (int x = 0; x < Chunk.WIDTH; x++)
        for (int z = 0; z < Chunk.DEPTH; z++)
        for (int y = 0; y < Chunk.HEIGHT; y++)
        {
            int light = chunk.GetSkyLight(x, y, z);
            if (light <= 1)
                continue;

            if (Spreads(chunk, x - 1, y, z, light) || Spreads(chunk, x + 1, y, z, light) ||
                Spreads(chunk, x, y, z - 1, light) || Spreads(chunk, x, y, z + 1, light) ||
                Spreads(chunk, x, y - 1, z, light) || Spreads(chunk, x, y + 1, z, light))
            {
                seeds.Add(new Vector3i(x, y, z));
            }
        }

        return seeds;
    }

    // In-chunk only: an out-of-range neighbour is another chunk's business.
    private bool Spreads(Chunk chunk, int x, int y, int z, int light)
    {
        if (x < 0 || x >= Chunk.WIDTH || y < 0 || y >= Chunk.HEIGHT || z < 0 || z >= Chunk.DEPTH)
            return false;

        return WouldBrighten(light, chunk.GetBlock(x, y, z), chunk.GetSkyLight(x, y, z));
    }

    // The same arithmetic TryPropagateSky uses to decide whether a write is worth doing. Opaque
    // neighbours fall out naturally: opacity 15 drives the result to the floor, which never exceeds
    // what's already stored.
    private bool WouldBrighten(int light, BlockType neighbourBlock, int neighbourLight) =>
        ClampSunLight(light - 1 - BlockRegistry.GetBlockOpacity(neighbourBlock)) > neighbourLight;

    /// <summary>
    /// Seeds light flow across the face two adjacent chunks share, in both directions. Each chunk's
    /// edge light was computed while the other didn't exist, so without this a cave crossing a chunk
    /// boundary is lit on one side and black on the other.
    ///
    /// Scans the face but enqueues only genuine differences, which in practice is a handful of
    /// voxels per border rather than the 2,048 examined.
    /// </summary>
    public void SeedAcrossBorder(Chunk a, Chunk b)
    {
        int dx = b.ChunkX - a.ChunkX, dz = b.ChunkZ - a.ChunkZ;

        // Only face-adjacent chunks share a border.
        if (Math.Abs(dx) + Math.Abs(dz) != 1)
            return;

        int aOriginX = a.ChunkX * Chunk.WIDTH, aOriginZ = a.ChunkZ * Chunk.DEPTH;
        int bOriginX = b.ChunkX * Chunk.WIDTH, bOriginZ = b.ChunkZ * Chunk.DEPTH;

        for (int t = 0; t < Chunk.WIDTH; t++)
        for (int y = 0; y < Chunk.HEIGHT; y++)
        {
            // (ax,az) is the voxel on a's edge; (bx,bz) is the one it touches on b's edge.
            int ax, az, bx, bz;
            if (dx != 0) { ax = dx > 0 ? Chunk.WIDTH - 1 : 0; bx = dx > 0 ? 0 : Chunk.WIDTH - 1; az = bz = t; }
            else         { az = dz > 0 ? Chunk.DEPTH - 1 : 0; bz = dz > 0 ? 0 : Chunk.DEPTH - 1; ax = bx = t; }

            int la = a.GetSkyLight(ax, y, az);
            int lb = b.GetSkyLight(bx, y, bz);

            if (la > 1 && WouldBrighten(la, b.GetBlock(bx, y, bz), lb))
                mSkyLightQueue.Enqueue(new LightNode(aOriginX + ax, y, aOriginZ + az));

            if (lb > 1 && WouldBrighten(lb, a.GetBlock(ax, y, az), la))
                mSkyLightQueue.Enqueue(new LightNode(bOriginX + bx, y, bOriginZ + bz));
        }
    }

    // Main thread only: both queues are shared across every chunk.
    private void CommitPendingEmitters(Chunk chunk)
    {
        int worldOffsetX = chunk.ChunkX * Chunk.WIDTH;
        int worldOffsetZ = chunk.ChunkZ * Chunk.DEPTH;

        if (chunk.PendingLightEmitters is { } emitters)
        {
            foreach (var e in emitters)
                mBlockLightQueue.Enqueue(new LightNode(worldOffsetX + e.X, e.Y, worldOffsetZ + e.Z));

            chunk.PendingLightEmitters = null;
        }

        if (chunk.PendingSkyLightSeeds is { } skySeeds)
        {
            foreach (var s in skySeeds)
                mSkyLightQueue.Enqueue(new LightNode(worldOffsetX + s.X, s.Y, worldOffsetZ + s.Z));

            chunk.PendingSkyLightSeeds = null;
        }
    }

    // PropagateAllSunlight/PropagateAllBlockLight lived here: one-time full-world BFS passes run
    // once after generating the whole fixed grid. There is no "whole world" to sweep any more.
    // CalculateInitialLighting seeds each chunk as the streamer integrates it, and World.Update
    // drains the queues incrementally from then on.

    /// <summary>
    /// Incrementally updates lighting after a block is placed at (x, y, z). If the new block is opaque
    /// (blocks light), any sky light and block light already sitting at that voxel is stripped and
    /// queued into the removal BFS (which will darken anything that was lit *through* this now-blocked
    /// voxel), and sky light is also stripped straight down the column below until another opaque block
    /// is hit (since that whole shaft was previously getting direct-from-above sun). If the new block
    /// itself emits light, that emission is seeded into the block-light add queue. This is the
    /// incremental (per-edit) counterpart to the one-time full-world passes above.
    /// </summary>
    public void OnBlockPlaced(int x, int y, int z, BlockType newBlock)
    {
        if (BlockRegistry.BlocksLight(newBlock))
        {
            byte oldSkyLight = (byte)mWorld.GetSkyLight(x, y, z);
            if (oldSkyLight > 0)
            {
                mWorld.SetSkyLightDirect(x, y, z, 0);
                mSkyRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, oldSkyLight));
                mWorld.MarkChunkDirtyAt(x, z);
            }

            for (int belowY = y - 1; belowY >= 0; belowY--)
            {
                if (BlockRegistry.BlocksLight(mWorld.GetBlock(x, belowY, z)))
                    break;

                int currentSkyLight = mWorld.GetSkyLight(x, belowY, z);
                if (currentSkyLight > 0)
                {
                    mWorld.SetSkyLightDirect(x, belowY, z, 0);
                    mSkyRemovalQueue.Enqueue(new LightRemovalNode(x, belowY, z, (byte)currentSkyLight));
                }
            }

            byte oldBlockLight = (byte)mWorld.GetBlockLight(x, y, z);
            if (oldBlockLight > 0)
            {
                mWorld.SetBlockLightDirect(x, y, z, 0);
                mBlockRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, oldBlockLight));
                mWorld.MarkChunkDirtyAt(x, z);
            }
        }

        int emission = Math.Min(BlockRegistry.Get(newBlock).LightEmission, mMaxBlockLight);
        if (emission > 0)
        {
            mWorld.SetBlockLightDirect(x, y, z, (byte)emission);
            mBlockLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);
        }
    }

    /// <summary>
    /// Incrementally updates lighting after a block is removed/broken at (x, y, z). If the removed
    /// block was itself an emitter, its block light is stripped and queued for removal-BFS darkening.
    /// Then the column above this voxel is scanned for any opaque block blocking the sky; if the sky
    /// is fully clear all the way up, this voxel (and the shaft below it, same downward walk as
    /// OnBlockPlaced) is granted full sunlight and re-seeded into the sky add-queue. If the sky is
    /// still blocked, sky light is instead pulled in sideways from whichever neighbor has the most
    /// (one step of falloff), so an opening in a cave wall lights up from an adjacent lit voxel rather
    /// than from directly overhead. Finally, if this block wasn't an emitter, block light is likewise
    /// pulled in from the brightest neighbor.
    /// </summary>
    public void OnBlockRemoved(int x, int y, int z, BlockType oldBlock)
    {
        int oldEmission = BlockRegistry.Get(oldBlock).LightEmission;
        if (oldEmission > 0)
        {
            byte currentBlockLight = (byte)mWorld.GetBlockLight(x, y, z);
            mWorld.SetBlockLightDirect(x, y, z, 0);
            mBlockRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, currentBlockLight));
            mWorld.MarkChunkDirtyAt(x, z);
        }

        bool hasSkyAccess = true;
        for (int checkY = y + 1; checkY < Chunk.HEIGHT; checkY++)
        {
            if (BlockRegistry.BlocksLight(mWorld.GetBlock(x, checkY, z)))
            {
                hasSkyAccess = false;
                break;
            }
        }

        if (hasSkyAccess)
        {
            // Give full sunlight using sunlight max
            mWorld.SetSkyLightDirect(x, y, z, (byte)mMaxSunLight);
            mSkyLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);

            for (int belowY = y - 1; belowY >= 0; belowY--)
            {
                if (BlockRegistry.BlocksLight(mWorld.GetBlock(x, belowY, z)))
                    break;

                mWorld.SetSkyLightDirect(x, belowY, z, (byte)mMaxSunLight);
                mSkyLightQueue.Enqueue(new LightNode(x, belowY, z));
            }
        }
        else if (oldEmission == 0)
        {
            int maxNeighborSkyLight = GetMaxNeighborSkyLight(x, y, z);
            if (maxNeighborSkyLight > 1)
            {
                int write = ClampSunLight(maxNeighborSkyLight - 1);
                mWorld.SetSkyLightDirect(x, y, z, (byte)write);
                mSkyLightQueue.Enqueue(new LightNode(x, y, z));
                mWorld.MarkChunkDirtyAt(x, z);
            }
        }

        if (oldEmission == 0)
        {
            int maxNeighborBlockLight = GetMaxNeighborBlockLight(x, y, z);
            if (maxNeighborBlockLight > 1)
            {
                mWorld.SetBlockLightDirect(x, y, z, (byte)(maxNeighborBlockLight - 1));
                mBlockLightQueue.Enqueue(new LightNode(x, y, z));
                mWorld.MarkChunkDirtyAt(x, z);
            }
        }
    }

    /// <summary>
    /// Drains a bounded amount of work from all four queues once per game tick, in priority order:
    /// sky removal, then block removal, then sky add (given half of whatever budget remains), then
    /// block add (given the rest). Removal passes run first so darkening a large area finishes before
    /// spending budget re-lighting it from neighboring, independently-lit sources. The overall budget
    /// is <see cref="MAX_UPDATES_PER_TICK"/> nodes, shared across all four queues per call.
    /// </summary>
    public void ProcessTick()
    {
        int updates = 0;

        while (mSkyRemovalQueue.Count > 0 && updates < MAX_UPDATES_PER_TICK)
        {
            updates++;
            ProcessSkyLightRemoval(mSkyRemovalQueue.Dequeue());
        }

        while (mBlockRemovalQueue.Count > 0 && updates < MAX_UPDATES_PER_TICK)
        {
            updates++;
            ProcessBlockLightRemoval(mBlockRemovalQueue.Dequeue());
        }

        int skyUpdates = ProcessSkyLightQueue((MAX_UPDATES_PER_TICK - updates) / 2);
        updates += skyUpdates;

        ProcessBlockLightQueue(MAX_UPDATES_PER_TICK - updates);
    }

    /// <summary>Returns the brightest sky light value among the 6 axis-aligned neighbors of (x, y, z).</summary>
    private int GetMaxNeighborSkyLight(int x, int y, int z)
    {
        int max = 0;
        max = Math.Max(max, mWorld.GetSkyLight(x + 1, y, z));
        max = Math.Max(max, mWorld.GetSkyLight(x - 1, y, z));
        max = Math.Max(max, mWorld.GetSkyLight(x, y + 1, z));
        max = Math.Max(max, mWorld.GetSkyLight(x, y - 1, z));
        max = Math.Max(max, mWorld.GetSkyLight(x, y, z + 1));
        max = Math.Max(max, mWorld.GetSkyLight(x, y, z - 1));
        return max;
    }

    /// <summary>Returns the brightest block light value among the 6 axis-aligned neighbors of (x, y, z).</summary>
    private int GetMaxNeighborBlockLight(int x, int y, int z)
    {
        int max = 0;
        max = Math.Max(max, mWorld.GetBlockLight(x + 1, y, z));
        max = Math.Max(max, mWorld.GetBlockLight(x - 1, y, z));
        max = Math.Max(max, mWorld.GetBlockLight(x, y + 1, z));
        max = Math.Max(max, mWorld.GetBlockLight(x, y - 1, z));
        max = Math.Max(max, mWorld.GetBlockLight(x, y, z + 1));
        max = Math.Max(max, mWorld.GetBlockLight(x, y, z - 1));
        return max;
    }

    /// <summary>
    /// Core sky light BFS step, run for up to <paramref name="maxUpdates"/> queued nodes. For each
    /// dequeued voxel, reads its current light level (nodes at &lt;= 1 can't spread any further and are
    /// skipped) and attempts to push (currentLight - 1) into all 6 axis-aligned neighbors via
    /// <see cref="TryPropagateSky"/>, which additionally subtracts the neighbor's own opacity. This is
    /// the -1-per-block falloff that makes light dim with distance from its source.
    /// </summary>
    private int ProcessSkyLightQueue(int maxUpdates)
    {
        int updates = 0;

        while (mSkyLightQueue.Count > 0 && updates < maxUpdates)
        {
            var node = mSkyLightQueue.Dequeue();
            updates++;

            int currentLight = mWorld.GetSkyLight(node.X, node.Y, node.Z);

            if (currentLight <= 1)
                continue;

            int newLight = currentLight - 1;
            TryPropagateSky(node.X + 1, node.Y, node.Z, newLight);
            TryPropagateSky(node.X - 1, node.Y, node.Z, newLight);
            TryPropagateSky(node.X, node.Y + 1, node.Z, newLight);
            TryPropagateSky(node.X, node.Y - 1, node.Z, newLight);
            TryPropagateSky(node.X, node.Y, node.Z + 1, newLight);
            TryPropagateSky(node.X, node.Y, node.Z - 1, newLight);
        }

        return updates;
    }

    /// <summary>
    /// Attempts to write <paramref name="newLight"/> (already decremented by 1 for the step taken)
    /// minus the destination block's opacity into (x, y, z). Only commits and re-enqueues the voxel
    /// if the result is strictly brighter than what's already stored there — this is what stops the
    /// BFS from running forever and makes it converge to the correct max-light-per-voxel result
    /// regardless of visit order. Out-of-world-height positions are ignored.
    /// </summary>
    private void TryPropagateSky(int x, int y, int z, int newLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int opacity = BlockRegistry.GetBlockOpacity(mWorld.GetBlock(x, y, z));
        int propagatedLight = ClampSunLight(newLight - opacity);

        if (propagatedLight > mWorld.GetSkyLight(x, y, z))
        {
            mWorld.SetSkyLightDirect(x, y, z, (byte)propagatedLight);
            mSkyLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);
        }
    }

    /// <summary>Expands one removal node to all 6 neighbors, darkening or re-lighting each as appropriate.</summary>
    private void ProcessSkyLightRemoval(LightRemovalNode node)
    {
        CheckSkyRemovalNeighbor(node.X + 1, node.Y, node.Z, node.OldLight);
        CheckSkyRemovalNeighbor(node.X - 1, node.Y, node.Z, node.OldLight);
        CheckSkyRemovalNeighbor(node.X, node.Y + 1, node.Z, node.OldLight);
        CheckSkyRemovalNeighbor(node.X, node.Y - 1, node.Z, node.OldLight);
        CheckSkyRemovalNeighbor(node.X, node.Y, node.Z + 1, node.OldLight);
        CheckSkyRemovalNeighbor(node.X, node.Y, node.Z - 1, node.OldLight);
    }

    /// <summary>
    /// Decides whether a neighbor was lit *by* the node being removed. If the neighbor's light is
    /// non-zero and strictly dimmer than the removed value, it must have been derived from this
    /// source, so it's zeroed and chained into the removal BFS as well. If instead the neighbor is at
    /// or above the removed value, it has its own independent light source (or is closer to one) and
    /// is re-enqueued into the normal add-queue so it can re-propagate light back into the gap just
    /// created — this is what lets sky light "heal" around a plugged hole.
    /// </summary>
    private void CheckSkyRemovalNeighbor(int x, int y, int z, byte oldLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int neighborLight = mWorld.GetSkyLight(x, y, z);

        if (neighborLight != 0 && neighborLight < oldLight)
        {
            mWorld.SetSkyLightDirect(x, y, z, 0);
            mSkyRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, (byte)neighborLight));
            mWorld.MarkChunkDirtyAt(x, z);
        }
        else if (neighborLight >= oldLight)
        {
            mSkyLightQueue.Enqueue(new LightNode(x, y, z));
        }
    }

    /// <summary>
    /// Core block light BFS step — identical shape to <see cref="ProcessSkyLightQueue"/> but operating
    /// on the block-light channel (torches, lava, glowstone, etc. instead of the sun).
    /// </summary>
    private int ProcessBlockLightQueue(int maxUpdates)
    {
        int updates = 0;

        while (mBlockLightQueue.Count > 0 && updates < maxUpdates)
        {
            var node = mBlockLightQueue.Dequeue();
            updates++;

            int currentLight = mWorld.GetBlockLight(node.X, node.Y, node.Z);

            if (currentLight <= 1)
                continue;

            int newLight = currentLight - 1;
            TryPropagateBlock(node.X + 1, node.Y, node.Z, newLight);
            TryPropagateBlock(node.X - 1, node.Y, node.Z, newLight);
            TryPropagateBlock(node.X, node.Y + 1, node.Z, newLight);
            TryPropagateBlock(node.X, node.Y - 1, node.Z, newLight);
            TryPropagateBlock(node.X, node.Y, node.Z + 1, newLight);
            TryPropagateBlock(node.X, node.Y, node.Z - 1, newLight);
        }

        return updates;
    }

    /// <summary>Block-light counterpart of <see cref="TryPropagateSky"/> — same falloff/opacity/early-out logic.</summary>
    private void TryPropagateBlock(int x, int y, int z, int newLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int opacity = BlockRegistry.GetBlockOpacity(mWorld.GetBlock(x, y, z));
        int propagatedLight = ClampBlockLight(newLight - opacity);

        if (propagatedLight > mWorld.GetBlockLight(x, y, z))
        {
            mWorld.SetBlockLightDirect(x, y, z, (byte)propagatedLight);
            mBlockLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);
        }
    }

    /// <summary>Block-light counterpart of <see cref="ProcessSkyLightRemoval"/>.</summary>
    private void ProcessBlockLightRemoval(LightRemovalNode node)
    {
        CheckBlockRemovalNeighbor(node.X + 1, node.Y, node.Z, node.OldLight);
        CheckBlockRemovalNeighbor(node.X - 1, node.Y, node.Z, node.OldLight);
        CheckBlockRemovalNeighbor(node.X, node.Y + 1, node.Z, node.OldLight);
        CheckBlockRemovalNeighbor(node.X, node.Y - 1, node.Z, node.OldLight);
        CheckBlockRemovalNeighbor(node.X, node.Y, node.Z + 1, node.OldLight);
        CheckBlockRemovalNeighbor(node.X, node.Y, node.Z - 1, node.OldLight);
    }

    /// <summary>Block-light counterpart of <see cref="CheckSkyRemovalNeighbor"/>.</summary>
    private void CheckBlockRemovalNeighbor(int x, int y, int z, byte oldLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int neighborLight = mWorld.GetBlockLight(x, y, z);

        if (neighborLight != 0 && neighborLight < oldLight)
        {
            mWorld.SetBlockLightDirect(x, y, z, 0);
            mBlockRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, (byte)neighborLight));
            mWorld.MarkChunkDirtyAt(x, z);
        }
        else if (neighborLight >= oldLight)
        {
            mBlockLightQueue.Enqueue(new LightNode(x, y, z));
        }
    }
}
