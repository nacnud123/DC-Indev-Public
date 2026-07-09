// Main file that manages the world. Has function to do world ticks, rebuild dirty chunk's meshes, render entities, initial generation, and get / set blocks | DA | 2/14/26

using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Rendering;
using VoxelEngine.Saving;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

/// <summary>
/// What kind of thing a <see cref="World.Raycast"/> call ended up hitting (or nothing at all).
/// </summary>
public enum RaycastHitType { None, Block, Entity }

/// <summary>
/// Result of casting a ray through the world (see World.Raycast.cs). Used for block picking (highlighting the block under the crosshair) and entity targeting (e.g. attacking a mob).
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
    /// The empty-space block position just before BlockPos along the ray - i.e. where a new block would be placed if the player right-clicked. Null if there was no previous step (e.g. the ray started inside a block).
    /// </summary>
    public Vector3i? PlacePos;
    /// <summary>Type of the block that was hit (only meaningful when Type == Block).</summary>
    public BlockType BlockType;
    /// <summary>The entity that was hit (only meaningful when Type == Entity).</summary>
    public Entity? Entity;

    /// <summary>Sentinel "nothing was hit" result - Distance is set to MaxValue so real hits always compare as closer.</summary>
    public static readonly RaycastHit Miss = new() { Type = RaycastHitType.None, Distance = float.MaxValue };
}

// Owns the whole voxel world: the grid of Chunks, all entities, lighting, and the queue of "dirty" chunks whose meshes need rebuilding. Split across a few files (World.cs, World.Raycast.cs, World.Ticks.cs) since there's a lot going on - "partial class" just means all those files together make up one World class.
public partial class World
{
    // V Not really needed anymore, but I don't feel like getting rid of it.
    public int SizeInChunks = 8;

    /// <summary>
    /// The currently-active World instance. Set in the constructor and cleared in Dispose(). Lets static helpers (GetBlockGlobal/SetBlockGlobal/etc.) reach the world without every caller needing to thread a World reference through.
    /// </summary>
    public static World? Current { get; private set; }

    // 2D grid of chunks, indexed [chunkX, chunkZ]. Chunk (0,0) covers world blocks x in [0,16), z in [0,16); chunk (1,0) covers x in [16,32), etc.
    private readonly Chunk[,] mChunks;
    private readonly LightingEngine mLightingEngine;
    private readonly Frustum mFrustum = new();
    private readonly List<Entity> mEntities = new();
    // Queue of pending scheduled block ticks: (world x, y, z, ticks remaining before it fires). Used for things like fluids that need to update again after a delay (e.g. water flowing).
    private readonly Queue<(int x, int y, int z, int countdown)> mBlockTickQueue = new();
    // Mirrors the positions currently queued in mBlockTickQueue, so ScheduleBlockTick can cheaply check "is this position already scheduled?" without scanning the whole queue.
    private readonly HashSet<(int, int, int)> mScheduledTickSet = new();
    // Chunks whose mesh needs rebuilding. Drained a few at a time each frame in Update() rather than all at once, to avoid a big lag spike (see MAX_CHUNK_REBUILDS_PER_FRAME).
    private readonly HashSet<Chunk> mDirtyChunks = new();
    private readonly Random mWorldRand;

    // Simple LCG state used by DoRandomTick to pick pseudo-random block positions per chunk cheaply (see the multiply/add constants in World.Ticks.cs).
    private int mRandomTickSeed;

    // Cap on how many dirty chunks get their mesh rebuilt in a single Update() call. Rebuilding a mesh walks every block in the chunk and re-uploads a new VBO, which is expensive, so spreading rebuilds across frames avoids a visible stutter after e.g. an explosion.
    private const int MAX_CHUNK_REBUILDS_PER_FRAME = 8;

    public Chunk[,] GetChunks() => mChunks;

    public TerrainGen TerrainGen;

    public IReadOnlyList<Entity> Entities => mEntities;

    /// <summary>
    /// Creates (or loads) a world of worldSize x worldSize chunks. First tries to load each chunk from disk (Serialization.Load); any chunk that has no save data is procedurally generated via TerrainGen using the given seed. Loading is done as a first pass over the whole grid before generation so that generation code can safely look at neighboring chunks that were loaded from disk.
    /// </summary>
    public World(int worldSize, int seed, WorldGenSettings settings = default)
    {
        SizeInChunks = worldSize;

        Current = this;
        mChunks = new Chunk[SizeInChunks, SizeInChunks];
        mLightingEngine = new LightingEngine(this);

        TerrainGen = new TerrainGen();
        TerrainGen.WorldSettings = settings;

        mWorldRand = new Random();
        mRandomTickSeed = mWorldRand.Next();

        // Tracks which chunks were successfully loaded from a save file, so the generation pass below knows to skip them.
        var loaded = new bool[SizeInChunks, SizeInChunks];

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                mChunks[x, z] = new Chunk(x, z, this);
                loaded[x, z] = Serialization.Load(mChunks[x, z]);
            }
        }

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                if (!loaded[x, z])
                    TerrainGen.GenerateChunk(this, x, z, seed);
            }
        }
    }

    /// <summary>
    /// One-time setup pass run right after world construction: seeds initial per-chunk lighting, propagates sunlight/block light across the whole grid, then builds every chunk's render mesh up front (instead of relying on the incremental per-frame dirty-chunk queue, since at startup literally every chunk is "dirty"). Also frees the terrain generator's scratch data once it's no longer needed.
    /// </summary>
    public void BuildAllMeshes()
    {
        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                mLightingEngine.CalculateInitialLighting(mChunks[x, z]);
            }
        }

        mLightingEngine.PropagateAllSunlight();
        mLightingEngine.PropagateAllBlockLight();

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                mChunks[x, z].RebuildMeshIfDirty();
            }
        }

        mDirtyChunks.Clear();

        // Release terrain generation data
        TerrainGen.ReleaseGenerationData();
    }

    /// <summary>
    /// Per-frame world maintenance: advances lighting propagation by one step if there's pending work, then rebuilds up to MAX_CHUNK_REBUILDS_PER_FRAME dirty chunk meshes. Called once per render frame (not per game tick) from the main loop.
    /// </summary>
    public void Update()
    {
        if (mLightingEngine.HasPendingUpdates)
            mLightingEngine.ProcessTick();

        // Rebuilding a chunk's mesh is expensive, so only rebuild a few dirty chunks per frame instead of all of them at once (which would cause a big lag spike after e.g. an explosion changes lots of blocks at once).
        int rebuilt = 0;
        mDirtyChunks.RemoveWhere(chunk =>
        {
            if (rebuilt >= MAX_CHUNK_REBUILDS_PER_FRAME)
                return false;

            if (!chunk.IsLoaded)
                return false;

            chunk.RebuildMeshIfDirty();
            rebuilt++;
            return true;
        });
    }

    /// <summary>
    /// Ticks every live entity, then sweeps the list backwards to dispose and remove any that died this tick. Iterating backwards means removals don't shift the index of elements not yet visited.
    /// </summary>
    public void TickEntities()
    {
        for (int e = 0; e < mEntities.Count; e++)
            mEntities[e].Tick(this);

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

    // Renders every entity within renderDistance of the camera (XZ distance only, so entities far above/below the camera but close on the ground plane still render). Uses squared distance to avoid a sqrt per entity per frame.
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

    // Convenience statics that route through World.Current, for code that doesn't have a World reference handy (e.g. static block behavior helpers). Fall back to sensible defaults (Air / full sky light / no block light) if there's no active world.
    public static BlockType GetBlockGlobal(int x, int y, int z) => Current?.GetBlock(x, y, z) ?? BlockType.Air;
    public static void SetBlockGlobal(int x, int y, int z, BlockType type) => Current?.SetBlock(x, y, z, type);
    public static int GetSkyLightGlobal(int x, int y, int z) => Current?.GetSkyLight(x, y, z) ?? 15;
    public static int GetBlockLightGlobal(int x, int y, int z) => Current?.GetBlockLight(x, y, z) ?? 0;

    /// <summary>
    /// Finds a safe spawn/teleport height at world column (x, z): scans downward from the top of the chunk and returns the position just above the first solid, non-leaf/non-wood block found (leaves/wood are skipped so spawning doesn't land the player inside a tree canopy or trunk overhang). Falls back to the vertical middle of the world if the column is entirely air (e.g. still ungenerated or a floating-world gap).
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

    // Converts a world block coordinate into (which chunk, position inside that chunk) and reads the block there. Chunks are 16 blocks wide, and 16 is a power of two, so ">> 4" (divide by 16) finds the chunk index and "& 15" (remainder) finds the position inside it - both faster than using / and % directly, and they also work correctly for negative coordinates.
    public BlockType GetBlock(int worldX, int worldY, int worldZ)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return BlockType.Air;

        int chunkX = worldX >> 4;
        int chunkZ = worldZ >> 4;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return BlockType.Air;

        return mChunks[chunkX, chunkZ].GetBlock(worldX & 15, worldY, worldZ & 15);
    }

    // Note: unlike GetBlock (which uses >> 4 / & 15 bit tricks), these coordinate-mapping methods use explicit division/modulo with a floor-toward-negative-infinity adjustment ("(worldX + 1) / Chunk.WIDTH - 1" for negative worldX). Both approaches are equivalent for power-of-two chunk sizes; this form is kept here for historical/readability reasons.
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

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            // Outside the loaded world grid: assume open sky.
            return 15;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;
        return mChunks[chunkX, chunkZ].GetSkyLight(localX, worldY, localZ);
    }

    // Writes a raw sky light value with no propagation - only the target voxel's own light nibble is touched. Used internally by LightingEngine's BFS propagation, which handles spreading the change to neighbors itself; callers outside the lighting engine should generally go through SetBlock instead so lighting stays consistent.
    public void SetSkyLightDirect(int worldX, int worldY, int worldZ, byte level)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;

        mChunks[chunkX, chunkZ].SetSkyLightDirect(localX, worldY, localZ, level);
    }

    // Block-emitted light (torches, lava, etc.), separate from sky light. Same out-of-bounds handling as GetSkyLight but defaults to 0 (no light) rather than 15, since there's no artificial light source outside the loaded world.
    public int GetBlockLight(int worldX, int worldY, int worldZ)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return 0;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return 0;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;
        return mChunks[chunkX, chunkZ].GetBlockLight(localX, worldY, localZ);
    }

    // Raw block-light write, same caveat as SetSkyLightDirect - intended for the lighting engine's own propagation, not general gameplay code.
    public void SetBlockLightDirect(int worldX, int worldY, int worldZ, byte level)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;

        mChunks[chunkX, chunkZ].SetBlockLightDirect(localX, worldY, localZ, level);
    }

    // Reads the 4-bit metadata nibble for the block at this world position (facing direction for stairs/torches/etc. - see Chunk's class comment for the nibble-packing scheme). Returns 0 (the "default facing" value) for out-of-range positions.
    public int GetMetadata(int worldX, int worldY, int worldZ)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return 0;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return 0;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;
        return mChunks[chunkX, chunkZ].GetMetadata(localX, worldY, localZ);
    }

    // Writes the metadata nibble and marks the owning chunk dirty (metadata changes affect mesh shape/texture orientation for stairs/torches, so the mesh must be rebuilt).
    public void SetMetadata(int worldX, int worldY, int worldZ, byte value)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;
        mChunks[chunkX, chunkZ].SetMetadata(localX, worldY, localZ, value);
        mChunks[chunkX, chunkZ].MarkDirty();
    }

    /// <summary>Looks up a chunk by chunk-grid coordinates; returns null if out of range.</summary>
    public Chunk? GetChunk(int chunkX, int chunkZ)
    {
        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return null;

        return mChunks[chunkX, chunkZ];
    }

    // Called by Chunk.MarkDirty() to register itself in the rebuild queue; kept as a World method (rather than Chunk managing its own dirty state globally) so World.Update can drain a bounded number of rebuilds per frame across the whole grid.
    public void NotifyDirty(Chunk chunk)
    {
        mDirtyChunks.Add(chunk);
    }

    // Marks the chunk containing (worldX, worldZ) dirty, and also marks any neighboring chunk whose border touches this position. This matters because chunk mesh building looks at neighboring chunks' blocks to decide whether to draw boundary faces (see IsTransparent) - if a block right at the edge changes, the chunk on the other side of that edge needs its mesh redone too, even though its own blocks didn't change.
    public void MarkChunkDirtyAt(int worldX, int worldZ)
    {
        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        mChunks[chunkX, chunkZ].MarkDirty();

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;

        if (localX == 0 && chunkX > 0)
            mChunks[chunkX - 1, chunkZ].MarkDirty();
        if (localX == Chunk.WIDTH - 1 && chunkX < SizeInChunks - 1)
            mChunks[chunkX + 1, chunkZ].MarkDirty();
        if (localZ == 0 && chunkZ > 0)
            mChunks[chunkX, chunkZ - 1].MarkDirty();
        if (localZ == Chunk.DEPTH - 1 && chunkZ < SizeInChunks - 1)
            mChunks[chunkX, chunkZ + 1].MarkDirty();
    }

    // "Direct" set: writes the block straight into the chunk array with none of SetBlock's side effects (no lighting update, no OnPlaced/OnRemoved hooks, no neighbor tick scheduling, no marking neighboring chunks dirty). Used by terrain generation and structure placement, where doing all that per-block during a big batch write would be wasteful - lighting/mesh building is done once afterward instead (see World.BuildAllMeshes).
    public void SetBlockDirect(int worldX, int worldY, int worldZ, BlockType type)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT)
            return;

        int chunkX = worldX >> 4;
        int chunkZ = worldZ >> 4;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        mChunks[chunkX, chunkZ].SetBlock(worldX & 15, worldY, worldZ & 15, type);
    }

    // Flags the chunk at this world position as having been modified since it was loaded/generated, so the saving system knows to write it back to disk (see Chunk.HasChunkBeenModified and Saving/Serialization).
    public void SetChunkAsModified(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT)
            return;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;

        mChunks[chunkX, chunkZ].HasChunkBeenModified = true;
    }

    // Force every currently-loaded chunk to be treated as modified, so a full save writes them all to disk regardless of whether they were actually touched (e.g. used for "save and quit" or migrating an old save format).
    public void MarkAllChunksWithBlocksAsModified()
    {
        for (int x = 0; x < SizeInChunks; x++)
            for (int z = 0; z < SizeInChunks; z++)
                mChunks[x, z].HasChunkBeenModified = true;
    }

    // The main way to change a block in the world. Unlike SetBlockDirect, this one also updates lighting and calls the old/new block's OnRemoved/OnPlaced hooks - e.g. this is what makes mining a TNT block (setting it to Air) trigger BlockTNT.OnRemoved and spawn the explosion.
    public void SetBlock(int worldX, int worldY, int worldZ, BlockType type)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT)
            return;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;

        var oldBlock = mChunks[chunkX, chunkZ].GetBlock(localX, worldY, localZ);
        mChunks[chunkX, chunkZ].SetBlock(localX, worldY, localZ, type);

        if (oldBlock != BlockType.Air)
        {
            // Recompute lighting from the old block being gone (e.g. removing a light source needs to darken its surroundings, removing an opaque block needs to let light in).
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

        // Fluids need to keep re-evaluating themselves (spreading/settling), so give the new fluid block its first scheduled tick immediately.
        if (BlockRegistry.IsFluid(type))
            ScheduleBlockTick(worldX, worldY, worldZ);

        // Let the 6 neighbors react too (e.g. a fluid neighbor might now be able to flow into the space that just changed, or a redstone-like block might need to re-evaluate).
        ScheduleNeighborTicks(worldX, worldY, worldZ);

        // If this change happened right on a chunk boundary, the neighboring chunk's mesh also depends on this block (see MarkChunkDirtyAt for the same pattern) - mark it dirty too.
        if (localX == 0 && chunkX > 0)
            mChunks[chunkX - 1, chunkZ].MarkDirty();
        if (localX == Chunk.WIDTH - 1 && chunkX < SizeInChunks - 1)
            mChunks[chunkX + 1, chunkZ].MarkDirty();
        if (localZ == 0 && chunkZ > 0)
            mChunks[chunkX, chunkZ - 1].MarkDirty();
        if (localZ == Chunk.DEPTH - 1 && chunkZ < SizeInChunks - 1)
            mChunks[chunkX, chunkZ + 1].MarkDirty();

        // A block was removed (turned to Air): check whether anything relied on it for support and knock those down too (e.g. torches, saplings sitting on top of the removed block).
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
                    Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(
                        new Vector3(worldX, worldY + 1, worldZ), above);
                }
            }

            // Wall torch metadata: 1=North, 2=South, 3=East, 4=West
            BreakUnsupportedWallTorch(worldX - 1, worldY, worldZ, 3); // East torch attached to this block
            BreakUnsupportedWallTorch(worldX + 1, worldY, worldZ, 4); // West torch attached to this block
            BreakUnsupportedWallTorch(worldX, worldY, worldZ - 1, 2); // South torch attached to this block
            BreakUnsupportedWallTorch(worldX, worldY, worldZ + 1, 1); // North torch attached to this block
        }
    }

    // Breaks the wall torch at (x,y,z) if it is still attached with the given facing metadata (expectedMeta). Called for each of the 4 horizontal neighbors of a block that was just removed, since a wall torch attached to that block's side would otherwise be left floating with nothing to hang on.
    private void BreakUnsupportedWallTorch(int x, int y, int z, int expectedMeta)
    {
        var block = GetBlock(x, y, z);
        if (block == BlockType.Torch && GetMetadata(x, y, z) == expectedMeta)
        {
            SetBlock(x, y, z, BlockType.Air);
            Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), block);
        }
    }

    // TODO: Update to use new tree generation / look Places a simple procedural tree centered at (x,y,z): a solid trunk of Wood blocks trunkHeight tall, plus a leavesRadius x (leavesMaxY-leavesMinY) leaf canopy around the upper part of the trunk. Only overwrites Air, so it won't clobber existing blocks. Uses SetBlock (not SetBlockDirect), so each leaf/wood block triggers normal lighting/mesh-dirty updates - fine for occasional tree growth, unlike bulk terrain gen.
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

    // Shared culling/iteration logic for both the opaque and transparent render passes. Also doubles as chunk load/unload bookkeeping: any chunk outside render distance gets IsLoaded = false (see World.Ticks.cs / DoScheduledTick, which skips unloaded chunks).
    private void RenderChunks(Camera camera, Action<Chunk> renderAction)
    {
        float renderDistSq = camera.RenderDistance * camera.RenderDistance;

        for (int x = 0; x < SizeInChunks; x++)
        {
            // Early row-skip: check if the entire row is too far on the X axis
            float rowCx = x * Chunk.WIDTH + Chunk.WIDTH * 0.5f;
            float rowDx = rowCx - camera.Position.X;
            if (rowDx * rowDx > renderDistSq)
            {
                for (int z2 = 0; z2 < SizeInChunks; z2++)
                    mChunks[x, z2].IsLoaded = false;
                continue;
            }

            for (int z = 0; z < SizeInChunks; z++)
            {
                var chunk = mChunks[x, z];

                float cx = chunk.ChunkX * Chunk.WIDTH + Chunk.WIDTH * 0.5f;
                float cz = chunk.ChunkZ * Chunk.DEPTH + Chunk.DEPTH * 0.5f;
                float dx = cx - camera.Position.X;
                float dz = cz - camera.Position.Z;

                if (dx * dx + dz * dz > renderDistSq)
                {
                    chunk.IsLoaded = false;
                    continue;
                }

                chunk.IsLoaded = true;

                Vector3 min = new(chunk.ChunkX * Chunk.WIDTH, 0, chunk.ChunkZ * Chunk.DEPTH);
                Vector3 max = new(min.X + Chunk.WIDTH, Chunk.HEIGHT, min.Z + Chunk.DEPTH);

                // Skip chunks outside the camera frustum even if they're within render distance.
                if (mFrustum.IsBoxVisible(min, max))
                    renderAction(chunk);
            }
        }
    }

    // Unculled variant: renders every chunk in the grid regardless of distance/frustum. Used by things like the isometric screenshot renderer (IsoScreenshot) that need the whole world visible rather than only what's near an in-game camera.
    public void RenderAll(Shader shader)
    {
        foreach (var chunk in mChunks)
        {
            if (chunk == null) 
                continue;

            shader.SetMatrix4("model", Matrix4x4.Identity);
            chunk.Render();
        }
    }

    public void RenderAllTransparent(Shader shader)
    {
        foreach (var chunk in mChunks)
        {
            if (chunk == null) 
                continue;

            shader.SetMatrix4("model", Matrix4x4.Identity);
            chunk.RenderTransparent();
        }
    }

    /// <summary>Releases GPU resources for every chunk and entity, and clears World.Current if it points at this instance.</summary>
    public void Dispose()
    {
        foreach (var entity in mEntities)
        {
            entity.Dispose();
        }
        mEntities.Clear();

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                mChunks[x, z].Dispose();
            }
        }

        if (Current == this)
            Current = null;
    }
}
