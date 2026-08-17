// Main file that manages the world. Has function to do world ticks, rebuild dirty chunk's meshes, render entities, initial generation, and get / set blocks | DA | 2/14/26

using System.Diagnostics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Rendering;
using VoxelEngine.Saving;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Terrain.Infinite;

namespace VoxelEngine.Terrain;

/// <summary>
/// What kind of thing a <see cref="World.Raycast"/> call ended up hitting (or nothing at all).
/// </summary>
public enum RaycastHitType
{
    None,
    Block,
    Entity
}

/// <summary>
/// Result of casting a ray through the world (see World.Raycast.cs). Used for block picking
/// (highlighting the block under the crosshair) and entity targeting (e.g. attacking a mob).
/// </summary>
public struct RaycastHit
{
    /// <summary>Whether the ray hit nothing, a block, or an entity.</summary>
    public RaycastHitType Type;

    /// <summary>Distance from the ray origin to the hit point, in world units.</summary>
    public float Distance;

    /// <summary>World-space position of the block that was hit (only meaningful when Type == Block).</summary>
    public Vector3i BlockPos;

    /// <summary>
    /// The empty-space block position just before BlockPos along the ray - i.e. where a new block
    /// would be placed if the player right-clicked. Null if there was no previous step (e.g. the
    /// ray started inside a block).
    /// </summary>
    public Vector3i? PlacePos;

    /// <summary>Type of the block that was hit (only meaningful when Type == Block).</summary>
    public BlockType BlockType;

    /// <summary>The entity that was hit (only meaningful when Type == Entity).</summary>
    public Entity? Entity;

    /// <summary>Sentinel "nothing was hit" result - Distance is set to MaxValue so real hits always compare as closer.</summary>
    public static readonly RaycastHit Miss = new() { Type = RaycastHitType.None, Distance = float.MaxValue };
}

// Owns the whole voxel world: the grid of Chunks, all entities, lighting, and the queue of
// "dirty" chunks whose meshes need rebuilding. Split across a few files (World.cs,
// World.Raycast.cs, World.Ticks.cs) since there's a lot going on - "partial class" just means
// all those files together make up one World class.
public partial class World
{
    // ⚠ No longer the size of the world - nothing on the infinite path reads it. The retired
    // fixed-area TerrainGen is the last reference, and it dies with that file.
    public int SizeInChunks = 8;

    // Streaming radius per player, in chunks. Not the camera's render distance - terrain keeps
    // loading past the fog so walking forward doesn't stall.
    public int ViewDistanceChunks { get; set; } = 8;

    /// <summary>
    /// The currently-active World instance. Set in the constructor and cleared in Dispose().
    /// Lets static helpers (GetBlockGlobal/SetBlockGlobal/etc.) reach the world without every
    /// caller needing to thread a World reference through.
    /// </summary>
    public static World? Current { get; private set; }

    private readonly LightingEngine mLightingEngine;
    private readonly Frustum mFrustum = new();
    private readonly List<Entity> mEntities = new();

    private readonly InfiniteWorldStreamer mChunkStreamer;
    public InfiniteWorldStreamer Streamer => mChunkStreamer;
    public IEnumerable<Chunk> LoadedChunks => mChunkStreamer.LoadedChunks;

    // Rebuilt in place each frame rather than reallocated - Update runs every frame and this would
    // otherwise be a steady drip of garbage for a list that is almost always one element long.
    private readonly List<ChunkObserver> mObservers = new();


    // Queue of pending scheduled block ticks: (world x, y, z, ticks remaining before it fires).
    // Used for things like fluids that need to update again after a delay (e.g. water flowing).
    private readonly Queue<(int x, int y, int z, int countdown)> mBlockTickQueue = new();

    // Mirrors the positions currently queued in mBlockTickQueue, so ScheduleBlockTick can
    // cheaply check "is this position already scheduled?" without scanning the whole queue.
    private readonly HashSet<(int, int, int)> mScheduledTickSet = new();

    // Chunks whose mesh needs rebuilding. Drained a few at a time each frame in Update()
    // rather than all at once, to avoid a big lag spike (see MAX_CHUNK_REBUILDS_PER_FRAME).
    private readonly HashSet<Chunk> mDirtyChunks = new();

    // Scratch list Update() sorts the dirty set into, nearest-first. Fields rather than locals so the
    // per-frame drain allocates nothing (a Sort closure would otherwise be built every frame).
    private readonly List<Chunk> mRebuildOrder = new();
    private readonly Comparison<Chunk> mNearestFirst;
    private Vector3 mRebuildSortCenter;

    private readonly Random mWorldRand;

    // Simple LCG state used by DoRandomTick to pick pseudo-random block positions per chunk
    // cheaply (see the multiply/add constants in World.Ticks.cs).
    private int mRandomTickSeed;

    // Cap on how many dirty chunks get their mesh rebuilt in a single Update() call. Rebuilding
    // a mesh walks every block in the chunk and re-uploads a new VBO, which is expensive, so
    // spreading rebuilds across frames avoids a visible stutter after e.g. an explosion.
    private const int MAX_CHUNK_REBUILDS_PER_FRAME = 8;

    // Wall-clock ceiling on mesh rebuilding per frame. A count alone is a poor budget: a chunk full
    // of terrain costs many times what an all-air one does, so "8 chunks" is anywhere from
    // negligible to a dropped frame. ~4 ms leaves headroom inside a 16 ms frame.
    private const double MAX_REBUILD_MILLIS_PER_FRAME = 4.0;

    // Wall-clock ceiling on light propagation per frame. One ProcessTick per Update (a 2048-node
    // batch every 50 ms) meant a torch's several-thousand-node flood took a visible handful of
    // frames, re-meshing the chunk at each partial stage. A time budget lets it settle in one.
    private const double MAX_LIGHT_MILLIS_PER_FRAME = 2.0;

    // Simulation residency (chunk.IsLoaded) is now decided by InfiniteWorldStreamer, in chunks
    // rather than world units, since it has to run the same test to know what to keep resident.
    public TerrainGen TerrainGen;

    public IReadOnlyList<Entity> Entities => mEntities;

    /// <summary>
    /// The seed this world was generated from. The constructor used to take it, hand it to
    /// TerrainGen, and forget it - but regenerating a chunk on demand (and telling a joining
    /// client which world it's looking at) both need it to survive.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// Every player in this world.
    ///
    /// Singleplayer keeps exactly one entry here and <c>IGameContext.GetPlayer</c> stays the
    /// convenient way to reach them. A server has many, and code that assumes "the player" is
    /// singleplayer-only logic by definition - this is what it should use instead.
    /// </summary>
    public readonly List<Player> Players = new();

    /// <summary>
    /// Seeds sunlight columns for a chunk and queues the BFS work, without exposing the whole
    /// LightingEngine. <c>Update()</c> already drains that queue each frame, so light bleeds
    /// across a newly arrived chunk's borders over the next few frames.
    /// </summary>
    public void CalculateInitialLightingFor(Chunk chunk) => mLightingEngine.CalculateInitialLighting(chunk);

    /// <summary>
    /// Worker-thread half of the above: does the two full-volume passes so the main thread only has
    /// to seed the BFS queue when the chunk arrives. Safe to call from a generation worker as long
    /// as no one else can see the chunk yet.
    /// </summary>
    public void SeedChunkLightingOffThread(Chunk chunk) => mLightingEngine.SeedInitialLightingOffThread(chunk);

    /// <summary>Lets sky light flow across the face two newly-adjacent chunks share.</summary>
    public void SeedLightAcrossBorder(Chunk a, Chunk b) => mLightingEngine.SeedAcrossBorder(a, b);

    /// <summary>Outstanding sky-light BFS work. Should stay bounded as you walk; see the F3-style diagnostics.</summary>
    public int PendingSkyLightNodes => mLightingEngine.PendingSkyLightNodes;

    // ---- Vector3i overloads ------------------------------------------------------------------
    //
    // The block-position type is Vector3i nearly everywhere it's computed (raycasts, block
    // entities, structures), and every call site was destructuring it back into three ints.
    public BlockType GetBlock(Vector3i p) => GetBlock(p.X, p.Y, p.Z);
    public void SetBlock(Vector3i p, BlockType t) => SetBlock(p.X, p.Y, p.Z, t);
    public void SetBlockDirect(Vector3i p, BlockType t) => SetBlockDirect(p.X, p.Y, p.Z, t);
    public int GetMetadata(Vector3i p) => GetMetadata(p.X, p.Y, p.Z);
    public void SetMetadata(Vector3i p, byte v) => SetMetadata(p.X, p.Y, p.Z, v);

    /// <summary>
    /// Creates an empty world. Nothing is generated here any more - the streamer pulls chunks in
    /// around players as they move. <paramref name="chunkSourceFactory"/> is what Stage 6 swaps for
    /// a network source; it's a factory rather than a plain IChunkSource because a source needs the
    /// World that doesn't exist until this constructor returns.
    /// </summary>
    public World(int seed, WorldGenSettings settings = default, Func<World, IChunkSource>? chunkSourceFactory = null)
    {
        Seed = seed;
        Current = this;

        mLightingEngine = new LightingEngine(this);
        mNearestFirst = (a, b) => DistanceToSortCenterSq(a).CompareTo(DistanceToSortCenterSq(b));

        TerrainGen = new TerrainGen();
        TerrainGen.WorldSettings = settings;

        mWorldRand = new Random();
        mRandomTickSeed = mWorldRand.Next();

        var factory = chunkSourceFactory ?? GenerateLocally;
        mChunkStreamer = new InfiniteWorldStreamer(this, factory(this));
    }

    private static IChunkSource GenerateLocally(World world) =>
        new GeneratingChunkSource(world, new InfiniteTerrainGenerator(world.Seed), Serialization.SaveLocation());

    /// <summary>
    /// Blocks until every chunk within <paramref name="radiusChunks"/> of <paramref name="center"/>
    /// is resident, so FindSpawnPosition has actual terrain to look at instead of falling through
    /// to its "empty column" default and dropping the player into the void.
    /// </summary>
    public void PrimeAround(Vector3 center, int radiusChunks, int timeoutMs = 30000)
    {
        var observers = new[] { new ChunkObserver(-1, center, radiusChunks) };
        var wanted = ChunkMath.CircleAround(ChunkMath.ToChunkCoord(center), radiusChunks).ToList();

        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            mChunkStreamer.Update(observers);

            if (wanted.TrueForAll(c => mChunkStreamer.GetChunk(c.X, c.Z) != null))
            {
                // Give sideways light a chance to settle before the first frame is drawn.
                for (int i = 0; i < 64 && mLightingEngine.HasPendingUpdates; i++)
                    mLightingEngine.ProcessTick();

                // Mesh it all now rather than trickling through Update's per-frame budget - this
                // runs behind the loading screen, where a stall costs nothing and holes in the
                // world for the first second cost a lot.
                foreach (var chunk in mChunkStreamer.LoadedChunks)
                    chunk.RebuildMeshIfDirty();

                return;
            }

            Thread.Sleep(1);
        }

        Console.WriteLine($"[World] PrimeAround({center}, r={radiusChunks}) timed out; starting anyway.");
    }

    /// <summary>
    /// Streams chunks in and out and sets <c>chunk.IsLoaded</c>. Belongs on the fixed 20 Hz tick -
    /// residency is simulation state, and the streamer's per-call budgets are tuned against that rate.
    /// </summary>
    public void TickStreaming(Vector3 simulationCenter)
    {
        mChunkStreamer.Update(BuildObservers(simulationCenter));
    }

    /// <summary>
    /// Per-frame world maintenance: drains light propagation, then rebuilds dirty chunk meshes
    /// nearest-first within a bounded budget. Must run once per render frame - on the 20 Hz tick it
    /// put a 50 ms floor under how fast a placed or broken block could appear.
    /// </summary>
    public void Update(Vector3 simulationCenter)
    {
        // Ahead of the rebuild drain, so meshes below see this edit's final light rather than a
        // half-propagated snapshot they'd have to rebuild again next frame.
        var lightTimer = Stopwatch.StartNew();
        while (mLightingEngine.HasPendingUpdates &&
               lightTimer.Elapsed.TotalMilliseconds < MAX_LIGHT_MILLIS_PER_FRAME)
        {
            mLightingEngine.ProcessTick();
        }

        // Drop chunks the streamer has since unloaded, or this set grows without bound as you walk
        // and eventually rebuilds meshes on disposed chunks.
        mDirtyChunks.RemoveWhere(chunk => mChunkStreamer.GetChunk(chunk.ChunkX, chunk.ChunkZ) != chunk);

        // Rebuilding a chunk's mesh is expensive, so only rebuild a few dirty chunks per frame
        // instead of all of them at once (which would cause a big lag spike after e.g. an
        // explosion changes lots of blocks at once).
        //
        // Nearest-first, because the budget makes this a queue and a HashSet has no useful order -
        // the chunk you just edited competed on equal terms with everything the streamer dirties as
        // you walk. The block under the crosshair is always in the nearest chunk.
        mRebuildSortCenter = simulationCenter;
        mRebuildOrder.Clear();
        mRebuildOrder.AddRange(mDirtyChunks);
        mRebuildOrder.Sort(mNearestFirst);

        int rebuilt = 0;
        var rebuildTimer = Stopwatch.StartNew();

        foreach (var chunk in mRebuildOrder)
        {
            if (rebuilt >= MAX_CHUNK_REBUILDS_PER_FRAME ||
                rebuildTimer.Elapsed.TotalMilliseconds >= MAX_REBUILD_MILLIS_PER_FRAME)
                break;

            // Deliberately not gated on chunk.IsLoaded. That flag means "close enough to simulate"
            // (radius 6), which is tighter than the streaming radius (8) - gating on it would leave
            // the outer ring of chunks permanently unmeshed, i.e. invisible until you walked at
            // them. Residency is already bounded by the streamer, so everything resident gets meshed.
            chunk.RebuildMeshIfDirty();
            mDirtyChunks.Remove(chunk);
            rebuilt++;
        }
    }

    // Squared XZ distance from a chunk's centre to Update's view centre - squared and
    // horizontal-only because it only has to order chunks, which span the full world height anyway.
    private float DistanceToSortCenterSq(Chunk chunk)
    {
        float dx = chunk.ChunkX * Chunk.WIDTH + Chunk.WIDTH * 0.5f - mRebuildSortCenter.X;
        float dz = chunk.ChunkZ * Chunk.DEPTH + Chunk.DEPTH * 0.5f - mRebuildSortCenter.Z;
        return dx * dx + dz * dz;
    }

    // One observer per player. simulationCenter is the fallback for the cases with no players
    // registered yet (world load, the iso screenshot renderer).
    private IReadOnlyList<ChunkObserver> BuildObservers(Vector3 simulationCenter)
    {
        mObservers.Clear();

        foreach (var player in Players)
            mObservers.Add(new ChunkObserver(player.Id, player.Position, ViewDistanceChunks));

        if (mObservers.Count == 0)
            mObservers.Add(new ChunkObserver(-1, simulationCenter, ViewDistanceChunks));

        return mObservers;
    }

    /// <summary>
    /// Ticks every live entity, then sweeps the list backwards to dispose and remove any that
    /// died this tick. Iterating backwards means removals don't shift the index of elements not
    /// yet visited.
    /// </summary>
    /// Set on a multiplayer client: fluids, gravity, growth and decay all run on the server, which
    /// sends the resulting block changes. See ScheduleBlockTick.
    public bool ServerDrivenBlockTicks;

    public void TickEntities()
    {
        for (int e = 0; e < mEntities.Count; e++)
        {
            if (mEntities[e].IsRemoteProxy)
                mEntities[e].TickProxy(this);
            else
                mEntities[e].Tick(this);
        }

        // Single pass: dispose and remove dead entities
        for (int e = mEntities.Count - 1; e >= 0; e--)
        {
            if (!mEntities[e].IsAlive)
            {
                mEntities[e].Dispose();
                mEntities.RemoveAt(e);
            }
        }
    }

    public void AddEntity(Entity entity)
    {
        mEntities.Add(entity);
    }

    public void RemoveEntity(Entity entity)
    {
        entity.IsAlive = false;
        mEntities.Remove(entity);
        entity.Dispose();
    }

    // Renders every entity within renderDistance of the camera (XZ distance only, so entities
    // far above/below the camera but close on the ground plane still render). Uses squared
    // distance to avoid a sqrt per entity per frame.
    public void RenderEntities(Matrix4x4 view, Matrix4x4 projection, Vector3 cameraPos, float renderDistance)
    {
        float renderDistSq = renderDistance * renderDistance;

        foreach (var entity in mEntities)
        {
            float dx = entity.Position.X - cameraPos.X;
            float dz = entity.Position.Z - cameraPos.Z;

            if (dx * dx + dz * dz > renderDistSq)
                continue;

            entity.Render(view, projection);
        }
    }

    // Convenience statics that route through World.Current, for code that doesn't have a World
    // reference handy (e.g. static block behavior helpers). Fall back to sensible defaults
    // (Air / full sky light / no block light) if there's no active world.
    /// <summary>
    /// Nearest player to <paramref name="position"/>, or null if nobody is in the world. Shared code
    /// must use this rather than <c>GameContext.Current.GetPlayer</c>, which is the *local* player
    /// and is null on a server - dereferencing it there takes the whole server down.
    /// </summary>
    public static Player? NearestPlayerTo(Vector3 position)
    {
        if (Current == null)
            return null;

        Player? nearest = null;
        float nearestSq = float.MaxValue;

        foreach (var player in Current.Players)
        {
            float distSq = Vector3.DistanceSquared(player.Position, position);
            if (distSq >= nearestSq)
                continue;

            nearestSq = distSq;
            nearest = player;
        }

        return nearest;
    }

    /// <summary>
    /// Distance to the nearest player, or <c>float.MaxValue</c> if there are none - which reads as
    /// "infinitely far" to the proximity-volume helpers, so mob sounds attenuate to silence instead
    /// of throwing.
    /// </summary>
    public static float DistanceToNearestPlayer(Vector3 position)
    {
        var nearest = NearestPlayerTo(position);
        return nearest == null ? float.MaxValue : (nearest.Position - position).Length();
    }

    public static BlockType GetBlockGlobal(int x, int y, int z) => Current?.GetBlock(x, y, z) ?? BlockType.Air;
    public static void SetBlockGlobal(int x, int y, int z, BlockType type) => Current?.SetBlock(x, y, z, type);
    public static int GetSkyLightGlobal(int x, int y, int z) => Current?.GetSkyLight(x, y, z) ?? 15;
    public static int GetBlockLightGlobal(int x, int y, int z) => Current?.GetBlockLight(x, y, z) ?? 0;

    /// <summary>
    /// Finds a safe spawn/teleport height at world column (x, z): scans downward from the top of
    /// the chunk and returns the position just above the first solid, non-leaf/non-wood block
    /// found (leaves/wood are skipped so spawning doesn't land the player inside a tree canopy or
    /// trunk overhang). Falls back to the vertical middle of the world if the column is entirely
    /// air (e.g. still ungenerated or a floating-world gap).
    /// </summary>
    public Vector3 FindSpawnPosition(int x, int z)
    {
        for (int y = Chunk.HEIGHT - 1; y >= 0; y--)
        {
            BlockType block = GetBlock(x, y, z);
            if (block == BlockType.Leaves || block == BlockType.Wood)
                continue;

            if (block != BlockType.Air && BlockRegistry.IsSolid(block))
                return new Vector3(x + 0.5f, y + 1, z + 0.5f);
        }

        return new Vector3(x + 0.5f, Chunk.HEIGHT / 2, z + 0.5f);
    }

    // Chunks are 16 wide, so ">> 4" picks the chunk and "& 15" the position inside it. Shift, not
    // divide: it floors toward negative infinity, which is what negative world coordinates need.
    // Returns null when that chunk isn't streamed in - every accessor below treats that exactly
    // like the old "outside the grid" case, so their fallbacks are unchanged.
    private Chunk? ChunkAt(int worldX, int worldZ) => mChunkStreamer.GetChunk(worldX >> 4, worldZ >> 4);

    public BlockType GetBlock(int worldX, int worldY, int worldZ)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return BlockType.Air;

        return ChunkAt(worldX, worldZ)?.GetBlock(worldX & 15, worldY, worldZ & 15) ?? BlockType.Air;
    }

    public int GetSkyLight(int worldX, int worldY, int worldZ)
    {
        switch (worldY)
        {
            case < 0:
                // Below the world: treat as solid ground in shadow.
                return 0;
            case >= Chunk.HEIGHT:
                // Above the world: fully lit by the sky.
                return 15;
        }

        // Not streamed in: assume open sky, same as the old out-of-grid answer.
        return ChunkAt(worldX, worldZ)?.GetSkyLight(worldX & 15, worldY, worldZ & 15) ?? 15;
    }

    // Writes a raw sky light value with no propagation - only the target voxel's own light nibble
    // is touched. Used internally by LightingEngine's BFS propagation, which handles spreading
    // the change to neighbors itself; callers outside the lighting engine should generally go
    // through SetBlock instead so lighting stays consistent.
    public void SetSkyLightDirect(int worldX, int worldY, int worldZ, byte level)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return;

        ChunkAt(worldX, worldZ)?.SetSkyLightDirect(worldX & 15, worldY, worldZ & 15, level);
    }

    // Block-emitted light (torches, lava, etc.), separate from sky light. Defaults to 0 rather
    // than 15, since there's no artificial light source outside the loaded world.
    public int GetBlockLight(int worldX, int worldY, int worldZ)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return 0;

        return ChunkAt(worldX, worldZ)?.GetBlockLight(worldX & 15, worldY, worldZ & 15) ?? 0;
    }

    // Raw block-light write, same caveat as SetSkyLightDirect.
    public void SetBlockLightDirect(int worldX, int worldY, int worldZ, byte level)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return;

        ChunkAt(worldX, worldZ)?.SetBlockLightDirect(worldX & 15, worldY, worldZ & 15, level);
    }

    // Reads the 4-bit metadata nibble (facing direction for stairs/torches/etc). Returns 0, the
    // "default facing" value, for anything out of range.
    public int GetMetadata(int worldX, int worldY, int worldZ)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return 0;

        return ChunkAt(worldX, worldZ)?.GetMetadata(worldX & 15, worldY, worldZ & 15) ?? 0;
    }

    // Writes the metadata nibble and marks the owning chunk dirty (metadata changes affect mesh
    // shape/texture orientation for stairs/torches, so the mesh must be rebuilt).
    public void SetMetadata(int worldX, int worldY, int worldZ, byte value)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return;

        var chunk = ChunkAt(worldX, worldZ);
        if (chunk == null)
            return;

        chunk.SetMetadata(worldX & 15, worldY, worldZ & 15, value);
        chunk.MarkDirty();

        RecordChange(worldX, worldY, worldZ, chunk.GetBlock(worldX & 15, worldY, worldZ & 15), (byte)GetMetadata(worldX, worldY, worldZ));
    }

    /// <summary>Looks up a chunk by chunk-grid coordinates; null if it isn't currently streamed in.</summary>
    public Chunk? GetChunk(int chunkX, int chunkZ) => mChunkStreamer.GetChunk(chunkX, chunkZ);

    // Called by Chunk.MarkDirty() to register itself in the rebuild queue; kept as a World method
    // (rather than Chunk managing its own dirty state globally) so World.Update can drain a
    // bounded number of rebuilds per frame across the whole grid.
    public void NotifyDirty(Chunk chunk)
    {
        mDirtyChunks.Add(chunk);
    }

    // Marks the chunk containing (worldX, worldZ) dirty, and also marks any neighboring chunk
    // whose border touches this position. This matters because chunk mesh building looks at
    // neighboring chunks' blocks to decide whether to draw boundary faces (see IsTransparent) -
    // if a block right at the edge changes, the chunk on the other side of that edge needs its
    // mesh redone too, even though its own blocks didn't change.
    public void MarkChunkDirtyAt(int worldX, int worldZ)
    {
        var chunk = ChunkAt(worldX, worldZ);
        if (chunk == null)
            return;

        chunk.MarkDirty();
        MarkBorderNeighborsDirty(worldX >> 4, worldZ >> 4, worldX & 15, worldZ & 15);
    }

    // A block on a chunk edge is visible from the neighbouring chunk's mesh too, so that mesh has
    // to be rebuilt as well. The null-safe lookups replace the old grid-edge bounds checks: a
    // neighbour that isn't streamed in needs no rebuild, and will mesh correctly when it arrives.
    private void MarkBorderNeighborsDirty(int chunkX, int chunkZ, int localX, int localZ)
    {
        if (localX == 0) mChunkStreamer.GetChunk(chunkX - 1, chunkZ)?.MarkDirty();
        if (localX == Chunk.WIDTH - 1) mChunkStreamer.GetChunk(chunkX + 1, chunkZ)?.MarkDirty();
        if (localZ == 0) mChunkStreamer.GetChunk(chunkX, chunkZ - 1)?.MarkDirty();
        if (localZ == Chunk.DEPTH - 1) mChunkStreamer.GetChunk(chunkX, chunkZ + 1)?.MarkDirty();
    }

    // "Direct" set: writes the block straight into the chunk array with none of SetBlock's side
    // effects (no lighting update, no OnPlaced/OnRemoved hooks, no neighbor tick scheduling, no
    // marking neighboring chunks dirty). Used by terrain generation and structure placement,
    // where doing all that per-block during a big batch write would be wasteful - lighting/mesh
    // building is done once afterward instead (see World.BuildAllMeshes).
    public void SetBlockDirect(int worldX, int worldY, int worldZ, BlockType type)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT)
            return;

        ChunkAt(worldX, worldZ)?.SetBlock(worldX & 15, worldY, worldZ & 15, type);

        RecordChange(worldX, worldY, worldZ, type, (byte)GetMetadata(worldX, worldY, worldZ));
    }

    /// A block change authored by the server: the block, its metadata and its LIGHTING, but none of
    /// the rules - no OnPlaced/OnRemoved, no scheduled ticks. The server ran those and sends whatever
    /// fell out of them; re-running them here would have the client inventing its own updates.
    public void SetBlockFromServer(int worldX, int worldY, int worldZ, BlockType type, byte metadata)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT)
            return;

        var chunk = ChunkAt(worldX, worldZ);
        if (chunk == null)
            return;

        int localX = worldX & 15, localZ = worldZ & 15;
        var oldBlock = chunk.GetBlock(localX, worldY, localZ);

        chunk.SetBlock(localX, worldY, localZ, type);
        chunk.SetMetadata(localX, worldY, localZ, metadata);

        if (oldBlock != BlockType.Air)
            mLightingEngine.OnBlockRemoved(worldX, worldY, worldZ, oldBlock);

        if (type != BlockType.Air)
            mLightingEngine.OnBlockPlaced(worldX, worldY, worldZ, type);

        chunk.MarkDirty();
        MarkBorderNeighborsDirty(worldX >> 4, worldZ >> 4, localX, localZ);
    }

    // Flags the chunk at this world position as having been modified since it was
    // loaded/generated, so the saving system knows to write it back to disk (see
    // Chunk.HasChunkBeenModified and Saving/Serialization).
    public void SetChunkAsModified(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT)
            return;

        var chunk = ChunkAt(worldX, worldZ);
        if (chunk != null)
            chunk.HasChunkBeenModified = true;
    }

    // Forces every resident chunk to be treated as modified so a full save writes them all out.
    // "All" now means "all currently streamed in" - chunks already unloaded were saved on the way
    // out if they needed it.
    public void MarkAllChunksWithBlocksAsModified()
    {
        foreach (var chunk in mChunkStreamer.LoadedChunks)
            chunk.HasChunkBeenModified = true;
    }

    // The main way to change a block in the world. Unlike SetBlockDirect, this one also updates
    // lighting and calls the old/new block's OnRemoved/OnPlaced hooks - e.g. this is what makes
    // mining a TNT block (setting it to Air) trigger BlockTNT.OnRemoved and spawn the explosion.
    public void SetBlock(int worldX, int worldY, int worldZ, BlockType type)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT)
            return;

        var chunk = ChunkAt(worldX, worldZ);
        if (chunk == null)
            return;

        int localX = worldX & 15;
        int localZ = worldZ & 15;

        var oldBlock = chunk.GetBlock(localX, worldY, localZ);
        chunk.SetBlock(localX, worldY, localZ, type);

        if (oldBlock != BlockType.Air)
        {
            // Recompute lighting from the old block being gone (e.g. removing a light source
            // needs to darken its surroundings, removing an opaque block needs to let light in).
            mLightingEngine.OnBlockRemoved(worldX, worldY, worldZ, oldBlock);
            if (type == BlockType.Air)
                BlockRegistry.Get(oldBlock).OnRemoved(this, worldX, worldY, worldZ);
        }

        if (type != BlockType.Air)
        {
            // New block may emit or block light - update lighting to account for it.
            mLightingEngine.OnBlockPlaced(worldX, worldY, worldZ, type);
        }

        if (type != BlockType.Air)
            BlockRegistry.Get(type).OnPlaced(this, worldX, worldY, worldZ);

        // Fluids need to keep re-evaluating themselves (spreading/settling), so give the new
        // fluid block its first scheduled tick immediately.
        if (BlockRegistry.IsFluid(type))
            ScheduleBlockTick(worldX, worldY, worldZ);

        // Let the 6 neighbors react too (e.g. a fluid neighbor might now be able to flow into
        // the space that just changed, or a redstone-like block might need to re-evaluate).
        ScheduleNeighborTicks(worldX, worldY, worldZ);

        MarkBorderNeighborsDirty(worldX >> 4, worldZ >> 4, localX, localZ);

        // A block was removed (turned to Air): check whether anything relied on it for support
        // and knock those down too (e.g. torches, saplings sitting on top of the removed block).
        if (oldBlock != BlockType.Air && type == BlockType.Air)
        {
            var above = GetBlock(worldX, worldY + 1, worldZ);
            if (above != BlockType.Air && BlockRegistry.NeedsSupportBelow(above))
            {
                // Wall torches (metadata > 0) don't need support below
                bool isWallTorch = above == BlockType.Torch && GetMetadata(worldX, worldY + 1, worldZ) > 0;
                if (!isWallTorch)
                {
                    SetBlock(worldX, worldY + 1, worldZ, BlockType.Air);
                    GameContext.Current?.ParticleSystem?.SpawnBlockBreakParticles(
                        new Vector3(worldX, worldY + 1, worldZ), above);
                }
            }

            // Wall torch metadata: 1=North, 2=South, 3=East, 4=West
            BreakUnsupportedWallTorch(worldX - 1, worldY, worldZ, 3); // East torch attached to this block
            BreakUnsupportedWallTorch(worldX + 1, worldY, worldZ, 4); // West torch attached to this block
            BreakUnsupportedWallTorch(worldX, worldY, worldZ - 1, 2); // South torch attached to this block
            BreakUnsupportedWallTorch(worldX, worldY, worldZ + 1, 1); // North torch attached to this block
        }

        RecordChange(worldX, worldY, worldZ, type, (byte)GetMetadata(worldX, worldY, worldZ));
    }

    // Breaks the wall torch at (x,y,z) if it is still attached with the given facing metadata
    // (expectedMeta). Called for each of the 4 horizontal neighbors of a block that was just
    // removed, since a wall torch attached to that block's side would otherwise be left floating
    // with nothing to hang on.
    private void BreakUnsupportedWallTorch(int x, int y, int z, int expectedMeta)
    {
        var block = GetBlock(x, y, z);
        if (block == BlockType.Torch && GetMetadata(x, y, z) == expectedMeta)
        {
            SetBlock(x, y, z, BlockType.Air);
            GameContext.Current?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), block);
        }
    }

    // TODO: Update to use new tree generation / look
    // Places a simple procedural tree centered at (x,y,z): a solid trunk of Wood blocks
    // trunkHeight tall, plus a leavesRadius x (leavesMaxY-leavesMinY) leaf canopy around the
    // upper part of the trunk. Only overwrites Air, so it won't clobber existing blocks. Uses
    // SetBlock (not SetBlockDirect), so each leaf/wood block triggers normal lighting/mesh-dirty
    // updates - fine for occasional tree growth, unlike bulk terrain gen.
    public void GrowTree(int x, int y, int z)
    {
        const int trunkHeight = 6;
        const int leavesRadius = 2;
        const int leavesMinY = 4;
        const int leavesMaxY = 8;

        for (int lx = -leavesRadius; lx <= leavesRadius; lx++)
        for (int ly = leavesMinY; ly <= leavesMaxY; ly++)
        for (int lz = -leavesRadius; lz <= leavesRadius; lz++)
            if (GetBlock(x + lx, y + ly, z + lz) == BlockType.Air)
                SetBlock(x + lx, y + ly, z + lz, BlockType.Leaves);

        for (int ty = 0; ty < trunkHeight; ty++)
            SetBlock(x, y + ty, z, BlockType.Wood);
    }

    /// <summary>Renders opaque chunk geometry visible from the camera (updates the frustum first).</summary>
    public void Render(Camera camera)
    {
        mFrustum.Update(camera.GetViewMatrix() * camera.GetProjectionMatrix());
        RenderChunks(camera, static chunk => chunk.Render());
    }

    /// <summary>Renders transparent chunk geometry (water/glass) as a second pass, reusing the frustum from Render().</summary>
    public void RenderTransparent(Camera camera)
    {
        RenderChunks(camera, static chunk => chunk.RenderTransparent());
    }

    // Shared culling/iteration logic for both the opaque and transparent render passes. Purely a
    // rendering concern - simulation residency (chunk.IsLoaded) is set by UpdateSimulationFlags
    // from the player's position, deliberately independent of how far the camera can see.
    private void RenderChunks(Camera camera, Action<Chunk> renderAction)
    {
        float renderDistSq = camera.RenderDistance * camera.RenderDistance;

        // The old row-skip optimisation went with the 2D array. It's no loss: iteration is now over
        // resident chunks only, which is already roughly what the distance check was pruning to.
        foreach (var chunk in mChunkStreamer.LoadedChunks)
        {
            float cx = chunk.ChunkX * Chunk.WIDTH + Chunk.WIDTH * 0.5f;
            float cz = chunk.ChunkZ * Chunk.DEPTH + Chunk.DEPTH * 0.5f;
            float dx = cx - camera.Position.X;
            float dz = cz - camera.Position.Z;

            if (dx * dx + dz * dz > renderDistSq)
                continue;

            Vector3 min = new(chunk.ChunkX * Chunk.WIDTH, 0, chunk.ChunkZ * Chunk.DEPTH);
            Vector3 max = new(min.X + Chunk.WIDTH, Chunk.HEIGHT, min.Z + Chunk.DEPTH);

            // Skip chunks outside the camera frustum even if they're within render distance.
            if (mFrustum.IsBoxVisible(min, max))
                renderAction(chunk);
        }
    }

    // Unculled variant: every resident chunk, no distance or frustum test. Used by the isometric
    // screenshot renderer, which wants everything loaded rather than what a camera can see.
    public void RenderAll()
    {
        foreach (var chunk in mChunkStreamer.LoadedChunks)
            chunk.Render();
    }

    public void RenderAllTransparent()
    {
        foreach (var chunk in mChunkStreamer.LoadedChunks)
            chunk.RenderTransparent();
    }

    /// <summary>Releases GPU resources for every chunk and entity, and clears World.Current if it points at this instance.</summary>
    public void Dispose()
    {
        foreach (var entity in mEntities)
        {
            entity.Dispose();
        }

        mEntities.Clear();

        // Saves anything modified on the way out, then disposes every resident chunk.
        mChunkStreamer.Dispose();
        mDirtyChunks.Clear();

        if (Current == this)
            Current = null;
    }
}