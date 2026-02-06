// Main world file, used to hold reference to lighting system, chunks, frustum, and stuff needed for world rendering. Also, has functions for the tick system and raycasts | DA | 2/5/26
using OpenTK.Mathematics;
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

public class World
{
    private const int RANDOM_DISPLAY_RADIUS = 16;
    private const int RANDOM_DISPLAY_ITERATIONS = 1000;
    private const int RANDOM_TICKS_PER_CHUNK = 3;

    public int SizeInChunks = 8;

    public static World? Current { get; private set; }

    private readonly Chunk[,] mChunks;
    private readonly LightingEngine mLightingEngine;
    private readonly Frustum mFrustum = new();
    private readonly List<Entity> mEntities = new();
    private readonly Random mRand;
    
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

        mRand = new Random();
        mRandomTickSeed = mRand.Next();

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                mChunks[x, z] = new Chunk(x, z, this);
            }
        }
    }

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

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                mChunks[x, z].RebuildMeshIfDirty();
            }
        }
    }

    public void Update()
    {
        if (mLightingEngine.HasPendingUpdates)
            mLightingEngine.ProcessTick();

        for (int x = 0; x < SizeInChunks; x++)
        {
            for (int z = 0; z < SizeInChunks; z++)
            {
                mChunks[x, z].RebuildMeshIfDirty();
            }
        }

        DoRandomTick();
    }

    public void RandomDisplayUpdates(Vector3 playerPos)
    {
        int px = (int)playerPos.X;
        int py = (int)playerPos.Y;
        int pz = (int)playerPos.Z;

        for (int i = 0; i < RANDOM_DISPLAY_ITERATIONS; i++)
        {
            int x = px + mRand.Next(-RANDOM_DISPLAY_RADIUS, RANDOM_DISPLAY_RADIUS + 1);
            int y = py + mRand.Next(-RANDOM_DISPLAY_RADIUS, RANDOM_DISPLAY_RADIUS + 1);
            int z = pz + mRand.Next(-RANDOM_DISPLAY_RADIUS, RANDOM_DISPLAY_RADIUS + 1);

            var blockType = GetBlock(x, y, z);
            if (blockType != BlockType.Air)
            {
                BlockRegistry.Get(blockType).RandomDisplayTick(x, y, z, mRand);
            }
        }
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

    public RaycastHit Raycast(Vector3 origin, Vector3 direction, float maxDist = 8f)
    {
        var blockHit = RaycastBlocks(origin, direction, maxDist);
        var entityHit = RaycastEntitiesInternal(origin, direction, maxDist);

        return entityHit.Distance < blockHit.Distance ? entityHit : blockHit;
    }

    private RaycastHit RaycastEntitiesInternal(Vector3 origin, Vector3 direction, float maxDistance)
    {
        var result = RaycastHit.Miss;

        foreach (var entity in mEntities)
        {
            if (entity.IsLookedAt(origin, direction, maxDistance, out float dist) && dist < result.Distance)
            {
                result = new RaycastHit
                {
                    Type = RaycastHitType.Entity,
                    Distance = dist,
                    Entity = entity
                };
            }
        }

        return result;
    }

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
    public static int GetLightGlobal(int x, int y, int z) => Current?.GetLight(x, y, z) ?? 15;

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
        if (worldY < 0 || worldY >= Chunk.HEIGHT) 
            return BlockType.Air;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return BlockType.Air;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;
        
        return mChunks[chunkX, chunkZ].GetBlock(localX, worldY, localZ);
    }

    public int GetLight(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0) 
            return 0;
        
        if (worldY >= Chunk.HEIGHT) 
            return 15;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return 15;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;
        return mChunks[chunkX, chunkZ].GetLight(localX, worldY, localZ);
    }

    public void SetLightDirect(int worldX, int worldY, int worldZ, byte level)
    {
        if (worldY < 0 || worldY >= Chunk.HEIGHT) 
            return;

        int chunkX = worldX >= 0 ? worldX / Chunk.WIDTH : (worldX + 1) / Chunk.WIDTH - 1;
        int chunkZ = worldZ >= 0 ? worldZ / Chunk.DEPTH : (worldZ + 1) / Chunk.DEPTH - 1;

        if (chunkX < 0 || chunkX >= SizeInChunks || chunkZ < 0 || chunkZ >= SizeInChunks)
            return;

        int localX = worldX - chunkX * Chunk.WIDTH;
        int localZ = worldZ - chunkZ * Chunk.DEPTH;
        
        mChunks[chunkX, chunkZ].SetLightDirect(localX, worldY, localZ, level);
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

        if (chunkX >= 0 && chunkX < SizeInChunks && chunkZ >= 0 && chunkZ < SizeInChunks)
            mChunks[chunkX, chunkZ].MarkDirty();
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

        if (oldBlock != BlockType.Air && type == BlockType.Air)
            mLightingEngine.OnBlockRemoved(worldX, worldY, worldZ, oldBlock);
        else if (type != BlockType.Air)
            mLightingEngine.OnBlockPlaced(worldX, worldY, worldZ, type);

        if (localX == 0 && chunkX > 0)
            mChunks[chunkX - 1, chunkZ].MarkDirty();
        if (localX == Chunk.WIDTH - 1 && chunkX < SizeInChunks - 1)
            mChunks[chunkX + 1, chunkZ].MarkDirty();
        if (localZ == 0 && chunkZ > 0)
            mChunks[chunkX, chunkZ - 1].MarkDirty();
        if (localZ == Chunk.DEPTH - 1 && chunkZ < SizeInChunks - 1)
            mChunks[chunkX, chunkZ + 1].MarkDirty();
    }

    private RaycastHit RaycastBlocks(Vector3 origin, Vector3 direction, float maxDist)
    {
        Vector3 dir = direction.Normalized();
        Vector3i current = new((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y), (int)MathF.Floor(origin.Z));
        Vector3i step = new(dir.X >= 0 ? 1 : -1, dir.Y >= 0 ? 1 : -1, dir.Z >= 0 ? 1 : -1);

        Vector3 tDelta = new(
            dir.X != 0 ? MathF.Abs(1f / dir.X) : float.MaxValue,
            dir.Y != 0 ? MathF.Abs(1f / dir.Y) : float.MaxValue,
            dir.Z != 0 ? MathF.Abs(1f / dir.Z) : float.MaxValue
        );

        Vector3 tMax = new(
            dir.X != 0 ? (dir.X > 0 ? current.X + 1 - origin.X : origin.X - current.X) * tDelta.X : float.MaxValue,
            dir.Y != 0 ? (dir.Y > 0 ? current.Y + 1 - origin.Y : origin.Y - current.Y) * tDelta.Y : float.MaxValue,
            dir.Z != 0 ? (dir.Z > 0 ? current.Z + 1 - origin.Z : origin.Z - current.Z) * tDelta.Z : float.MaxValue
        );

        float dist = 0;
        Vector3i? prev = null;

        while (dist < maxDist)
        {
            var block = GetBlock(current.X, current.Y, current.Z);
            if (block != BlockType.Air)
            {
                var pos = new Vector3(current.X, current.Y, current.Z);
                var min = BlockRegistry.GetBoundsMin(block) + pos;
                var max = BlockRegistry.GetBoundsMax(block) + pos;

                if (RayIntersectsAabb(origin, dir, min, max, out float hitDist) && hitDist <= maxDist)
                {
                    return new RaycastHit
                    {
                        Type = RaycastHitType.Block,
                        Distance = hitDist,
                        BlockPos = current,
                        PlacePos = prev,
                        BlockType = block
                    };
                }
            }

            prev = current;

            if (tMax.X < tMax.Y && tMax.X < tMax.Z)
            {
                current.X += step.X;
                dist = tMax.X;
                tMax.X += tDelta.X;
            }
            else if (tMax.Y < tMax.Z)
            {
                current.Y += step.Y;
                dist = tMax.Y;
                tMax.Y += tDelta.Y;
            }
            else
            {
                current.Z += step.Z;
                dist = tMax.Z;
                tMax.Z += tDelta.Z;
            }
        }

        return RaycastHit.Miss;
    }

    private static bool RayIntersectsAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float hitDist)
    {
        float tmin = 0, tmax = float.MaxValue;
        hitDist = float.MaxValue;

        if (!SlabIntersect(origin.X, dir.X, min.X, max.X, ref tmin, ref tmax)) return false;
        if (!SlabIntersect(origin.Y, dir.Y, min.Y, max.Y, ref tmin, ref tmax)) return false;
        if (!SlabIntersect(origin.Z, dir.Z, min.Z, max.Z, ref tmin, ref tmax)) return false;

        hitDist = tmin;
        return true;
    }

    private static bool SlabIntersect(float origin, float dir, float min, float max, ref float tmin, ref float tmax)
    {
        if (MathF.Abs(dir) < 1e-6f)
            return origin >= min && origin <= max;

        float t1 = (min - origin) / dir;
        float t2 = (max - origin) / dir;

        if (t1 > t2) (t1, t2) = (t2, t1);

        tmin = MathF.Max(tmin, t1);
        tmax = MathF.Min(tmax, t2);

        return tmin <= tmax;
    }

    public void Render(Camera camera)
    {
        mFrustum.Update(camera.GetViewMatrix() * camera.GetProjectionMatrix());
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
                    continue;

                Vector3 min = new(chunk.ChunkX * Chunk.WIDTH, 0, chunk.ChunkZ * Chunk.DEPTH);
                Vector3 max = new(min.X + Chunk.WIDTH, Chunk.HEIGHT, min.Z + Chunk.DEPTH);

                if (mFrustum.IsBoxVisible(min, max))
                    chunk.Render();
            }
        }
    }
    
    private void DoRandomTick()
    {
        for (int cx = 0; cx < SizeInChunks; cx++)
        {
            for (int cz = 0; cz < SizeInChunks; cz++)
            {
                var chunk = mChunks[cx, cz];

                for (int i = 0; i < RANDOM_TICKS_PER_CHUNK; i++)
                {
                    mRandomTickSeed = mRandomTickSeed * 3 + 1013904223;

                    int rX = (mRandomTickSeed >> 2) & (Chunk.WIDTH - 1);
                    int rZ = (mRandomTickSeed >> 6) & (Chunk.DEPTH - 1);
                    int rY = (mRandomTickSeed >> 10) & (Chunk.HEIGHT - 1);

                    BlockType blockType = chunk.GetBlock(rX, rY, rZ);

                    if (blockType != BlockType.Air && BlockRegistry.TicksRandomly(blockType))
                    {
                        int worldX = cx * Chunk.WIDTH + rX;
                        int worldZ = cz * Chunk.DEPTH + rZ;
                        BlockRegistry.Get(blockType).RandomTick(this, worldX, rY, worldZ, mRand);
                    }
                }
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