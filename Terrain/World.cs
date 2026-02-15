// Main file that manages the world. Has functions the do world ticks, rebuild dirty chunk's meshes, render entities, initial generation, and get / set blocks | DA | 2/14/26
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public enum RaycastHitType { None, Block, Entity }

public struct RaycastHit
{
    public RaycastHitType Type;
    public float Distance;
    public Vector3i BlockPos;
    public Vector3i? PlacePos;
    public BlockType BlockType;
    public Entity? Entity;

    public static readonly RaycastHit Miss = new() { Type = RaycastHitType.None, Distance = float.MaxValue };
}

public partial class World
{
    public int SizeInChunks = 8;

    public static World? Current { get; private set; }

    private readonly Chunk[,] mChunks;
    private readonly LightingEngine mLightingEngine;
    private readonly Frustum mFrustum = new();
    private readonly List<Entity> mEntities = new();
    private readonly Queue<(int x, int y, int z)> mBlockTickQueue = new();
    private readonly Random mWorldRand;

    private int mRandomTickSeed;

    public TerrainGen TerrainGen;

    public IReadOnlyList<Entity> Entities => mEntities;

    public World(int worldSize)
    {
        SizeInChunks = worldSize;

        Current = this;
        mChunks = new Chunk[SizeInChunks, SizeInChunks];
        mLightingEngine = new LightingEngine(this);

        TerrainGen = new TerrainGen();

        mWorldRand = new Random();
        mRandomTickSeed = mWorldRand.Next();

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                mChunks[x, z] = new Chunk(x, z, this);
            }
        }

        int seed = DateTime.Now.Second;
        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                TerrainGen.GenerateChunk(this, x, z, seed);
            }
        }
    }

    // Build all the initial chunks lighting and meshes
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
    }

    // Process lighting ticks, rebuild the mesh if dirty, and does random ticks.
    public void Update()
    {
        if (mLightingEngine.HasPendingUpdates)
            mLightingEngine.ProcessTick();

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                if (mChunks[x, z].IsLoaded)
                    mChunks[x, z].RebuildMeshIfDirty();
            }
        }

        DoRandomTick();
    }

    public void TickEntities()
    {
        foreach (var entity in mEntities)
        {
            entity.Tick(this);
        }

        int removed = mEntities.RemoveAll(e => !e.IsAlive);
        if (removed > 0)
            mEntities.TrimExcess();
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

    // Render all the entities
    public void RenderEntities(Matrix4 view, Matrix4 projection, Vector3 cameraPos, float renderDistance)
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

    public static BlockType GetBlockGlobal(int x, int y, int z) => Current?.GetBlock(x, y, z) ?? BlockType.Air;
    public static void SetBlockGlobal(int x, int y, int z, BlockType type) => Current?.SetBlock(x, y, z, type);
    public static int GetSkyLightGlobal(int x, int y, int z) => Current?.GetSkyLight(x, y, z) ?? 15;
    public static int GetBlockLightGlobal(int x, int y, int z) => Current?.GetBlockLight(x, y, z) ?? 0;

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

    public BlockType GetBlock(int worldX, int worldY, int worldZ)
    {
        if (worldY is < 0 or >= Chunk.HEIGHT)
            return BlockType.Air;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return BlockType.Air;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;

        return mChunks[chunkX, chunkZ].GetBlock(localX, worldY, localZ);
    }

    public int GetSkyLight(int worldX, int worldY, int worldZ)
    {
        switch (worldY)
        {
            case < 0:
                return 0;
            case >= Chunk.HEIGHT:
                return 15;
        }

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return 15;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;
        return mChunks[chunkX, chunkZ].GetSkyLight(localX, worldY, localZ);
    }

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

    public Chunk? GetChunk(int chunkX, int chunkZ)
    {
        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return null;

        return mChunks[chunkX, chunkZ];
    }

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

    public void SetBlockDirect(int worldX, int worldY, int worldZ, BlockType type)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT)
            return;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;

        mChunks[chunkX, chunkZ].SetBlock(localX, worldY, localZ, type);
    }

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
            mLightingEngine.OnBlockRemoved(worldX, worldY, worldZ, oldBlock);
            if (type == BlockType.Air)
                BlockRegistry.Get(oldBlock).OnRemoved(this, worldX, worldY, worldZ);
        }

        if (type != BlockType.Air)
        {
            mLightingEngine.OnBlockPlaced(worldX, worldY, worldZ, type);
        }

        if (type != BlockType.Air)
            BlockRegistry.Get(type).OnPlaced(this, worldX, worldY, worldZ);

        if (BlockRegistry.TicksRandomly(type))
            ScheduleBlockTick(worldX, worldY, worldZ);

        if (type == BlockType.Air)
            ScheduleNeighborTicks(worldX, worldY, worldZ);

        if (localX == 0 && chunkX > 0)
            mChunks[chunkX - 1, chunkZ].MarkDirty();
        if (localX == Chunk.WIDTH - 1 && chunkX < SizeInChunks - 1)
            mChunks[chunkX + 1, chunkZ].MarkDirty();
        if (localZ == 0 && chunkZ > 0)
            mChunks[chunkX, chunkZ - 1].MarkDirty();
        if (localZ == Chunk.DEPTH - 1 && chunkZ < SizeInChunks - 1)
            mChunks[chunkX, chunkZ + 1].MarkDirty();

        if (oldBlock != BlockType.Air && type == BlockType.Air)
        {
            var above = GetBlock(worldX, worldY + 1, worldZ);
            if (above != BlockType.Air && BlockRegistry.NeedsSupportBelow(above))
            {
                SetBlock(worldX, worldY + 1, worldZ, BlockType.Air);
                Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(
                    new Vector3(worldX, worldY + 1, worldZ), above);
            }

            BreakUnsupportedWallTorch(worldX - 1, worldY, worldZ, BlockType.TorchEast);
            BreakUnsupportedWallTorch(worldX + 1, worldY, worldZ, BlockType.TorchWest);
            BreakUnsupportedWallTorch(worldX, worldY, worldZ - 1, BlockType.TorchSouth);
            BreakUnsupportedWallTorch(worldX, worldY, worldZ + 1, BlockType.TorchNorth);
        }
    }

    private void BreakUnsupportedWallTorch(int x, int y, int z, BlockType expectedTorch)
    {
        var block = GetBlock(x, y, z);
        if (block == expectedTorch)
        {
            SetBlock(x, y, z, BlockType.Air);
            Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), block);
        }
    }

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

    public void Render(Camera camera)
    {
        mFrustum.Update(camera.GetViewMatrix() * camera.GetProjectionMatrix());
        RenderChunks(camera, static chunk => chunk.Render());
    }

    public void RenderTransparent(Camera camera)
    {
        RenderChunks(camera, static chunk => chunk.RenderTransparent());
    }

    private void RenderChunks(Camera camera, Action<Chunk> renderAction)
    {
        float renderDistSq = camera.RenderDistance * camera.RenderDistance;

        for (int x = 0; x < SizeInChunks; x++)
        {
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

                if (mFrustum.IsBoxVisible(min, max))
                    renderAction(chunk);
            }
        }
    }

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
