// Distance-based mob spawner. Every tick tries Minecraft-style random spawning across the world. Hostiles prefer low Y (underground) + darkness gate; passives require bright light + low density. | DA | 3/5/26
using OpenTK.Mathematics;
using VoxelEngine.GameEntity;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Core;

public class MobSpawner
{
    private const int MIN_PLAYER_DIST_BLOCKS = 32;
    private const int HOSTILE_SPAWN_LOOPS_PER_TICK = 4;

    private const int OUTER_SCATTER_ATTEMPTS = 2;
    private const int INNER_SCATTER_ATTEMPTS = 3;
    private const int XZ_JITTER = 5;
    private const int Y_JITTER = 1;
    
    private const int PASSIVE_SPAWN_LOOPS_PER_TICK = 1;
    
    private const float HOSTILE_CAP_MULTIPLIER = 1.0f;
    private readonly World mWorld;
    private readonly Random mRandom;

    public MobSpawner(World world, Random random)
    {
        mWorld = world;
        mRandom = random;
    }

    public void Tick()
    {
        int hostileCount = 0;
        int passiveCount = 0;

        foreach (var e in mWorld.Entities)
        {
            if (!e.IsAlive) 
                continue;

            if (e is Zombie or Skeleton or Stalker or Spider) 
                hostileCount++;
            else if (e is Pig or Sheep) 
                passiveCount++;
        }

        int hostileCap = GetHostileCap();
        int passiveCap = GetPassiveCap();

        bool canSpawnHostiles = hostileCount < hostileCap;
        bool canSpawnPassives = passiveCount < passiveCap;

        if (!canSpawnHostiles && !canSpawnPassives)
            return;

        Vector3 playerPos = Game.Instance.GetPlayer.Position;
        
        float sunAngle = Game.Instance.TimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0f, 1f);
        int skylightSubtracted = (int)MathHelper.Lerp(15f, 4f, sunlightLevel);

        if (canSpawnHostiles)
        {
            for (int i = 0; i < HOSTILE_SPAWN_LOOPS_PER_TICK; i++)
            {
                if (hostileCount >= hostileCap)
                    break;

                if (TrySpawnCategory(
                        playerPos,
                        skylightSubtracted,
                        biasedLowY: true,
                        spawnHostile: true))
                {
                    hostileCount++;
                }
            }
        }

        if (canSpawnPassives)
        {
            for (int i = 0; i < PASSIVE_SPAWN_LOOPS_PER_TICK; i++)
            {
                if (passiveCount >= passiveCap)
                    break;

                if (TrySpawnCategory(
                        playerPos,
                        skylightSubtracted,
                        biasedLowY: false,
                        spawnHostile: false))
                {
                    passiveCount++;
                }
            }
        }
    }
    
    private bool TrySpawnCategory(Vector3 playerPos, int skylightSubtracted, bool biasedLowY, bool spawnHostile)
    {
        int worldSize = mWorld.SizeInChunks * Chunk.WIDTH;
        if (worldSize <= 0)
            return false;

        int ax = mRandom.Next(worldSize);
        int az = mRandom.Next(worldSize);

        int ay = biasedLowY ? NextBiasedLowY() : mRandom.Next(1, Chunk.HEIGHT - 2);

        for (int outer = 0; outer < OUTER_SCATTER_ATTEMPTS; outer++)
        {
            for (int inner = 0; inner < INNER_SCATTER_ATTEMPTS; inner++)
            {
                int x = ax + mRandom.Next(-XZ_JITTER, XZ_JITTER + 1);
                int y = ay + mRandom.Next(-Y_JITTER, Y_JITTER + 1);
                int z = az + mRandom.Next(-XZ_JITTER, XZ_JITTER + 1);

                if (!InBounds(x, y, z, worldSize))
                    continue;

                // Must be far enough from the player (MC: distance^2 > 1024)
                float dx = (x + 0.5f) - playerPos.X;
                float dy = (y + 0.0f) - playerPos.Y;
                float dz = (z + 0.5f) - playerPos.Z;
                if (dx * dx + dy * dy + dz * dz <= MIN_PLAYER_DIST_BLOCKS * MIN_PLAYER_DIST_BLOCKS)
                    continue;

                if (!MeetsBlockPlacementRules(x, y, z))
                    continue;

                int skyLight = mWorld.GetSkyLight(x, y, z);
                int blockLight = mWorld.GetBlockLight(x, y, z);
                int effectiveLight = Math.Max(0, Math.Max(blockLight, skyLight - skylightSubtracted));
                
                if (spawnHostile)
                {
                    if (effectiveLight > mRandom.Next(8))
                        continue;

                    Vector3 spawnPos = new(x + 0.5f, y, z + 0.5f);
                    Entity mob = CreateHostile(spawnPos);

                    if (!CanSpawnEntityHere(mob))
                        continue;

                    mWorld.AddEntity(mob);
                    return true;
                }
                else
                {
                    if (effectiveLight <= 8)
                        continue;

                    Vector3 spawnPos = new(x + 0.5f, y, z + 0.5f);
                    Entity mob = CreatePassive(spawnPos);

                    if (!CanSpawnEntityHere(mob))
                        continue;

                    mWorld.AddEntity(mob);
                    return true;
                }
            }
        }

        return false;
    }

    private int GetHostileCap()
    {
        int width = mWorld.SizeInChunks * Chunk.WIDTH;
        int length = width;
        int height = Chunk.HEIGHT;

        double baseCap = (width * (double)length * height) * 20.0 / (64.0 * 64.0 * 64.0) / 2.0;
        int cap = (int)Math.Floor(baseCap * HOSTILE_CAP_MULTIPLIER);

        return Math.Max(0, cap);
    }

    private int GetPassiveCap()
    {
        int width = mWorld.SizeInChunks * Chunk.WIDTH;
        int length = width;

        int cap = (int)Math.Floor(width * (double)length / 4000.0);
        return Math.Max(0, cap);
    }

    private int NextBiasedLowY()
    {
        int a = mRandom.Next(1, Chunk.HEIGHT - 2);
        int b = mRandom.Next(1, Chunk.HEIGHT - 2);
        return Math.Min(a, b);
    }

    private static bool InBounds(int x, int y, int z, int worldSize) =>
        x >= 0 && x < worldSize &&
        z >= 0 && z < worldSize &&
        y >= 1 && y < Chunk.HEIGHT - 2;

    private bool MeetsBlockPlacementRules(int x, int y, int z)
    {
        var at = mWorld.GetBlock(x, y, z);
        var above = mWorld.GetBlock(x, y + 1, z);
        var below = mWorld.GetBlock(x, y - 1, z);

        if (at != BlockType.Air) 
            return false;
        
        if (above != BlockType.Air) 
            return false;

        if (!BlockRegistry.IsSolid(below)) 
            return false;

        // Not in liquid
        if (below == BlockType.Water || below == BlockType.Lava) 
            return false;
        
        if (at == BlockType.Water || at == BlockType.Lava) 
            return false;
        
        if (above == BlockType.Water || above == BlockType.Lava) 
            return false;

        return true;
    }

    private bool CanSpawnEntityHere(Entity e)
    {
        Aabb box = e.GetBoundingBox();

        if (!IsAabbClearOfSolids(box))
            return false;

        foreach (var other in mWorld.Entities)
        {
            if (!other.IsAlive) 
                continue;
            
            if (ReferenceEquals(other, e)) 
                continue;
            
            if (other.GetBoundingBox().Intersects(box))
                return false;
        }

        // Safety: reject if feet block is fluid (in case bounding box ends up inside a fluid edge case)
        int fx = (int)MathF.Floor(e.Position.X);
        int fy = (int)MathF.Floor(e.Position.Y);
        int fz = (int)MathF.Floor(e.Position.Z);
        var foot = mWorld.GetBlock(fx, fy, fz);
        
        if (foot == BlockType.Water || foot == BlockType.Lava)
            return false;

        return true;
    }

    private bool IsAabbClearOfSolids(Aabb box)
    {
        int minX = (int)MathF.Floor(box.Min.X);
        int minY = (int)MathF.Floor(box.Min.Y);
        int minZ = (int)MathF.Floor(box.Min.Z);

        int maxX = (int)MathF.Floor(box.Max.X);
        int maxY = (int)MathF.Floor(box.Max.Y);
        int maxZ = (int)MathF.Floor(box.Max.Z);

        int worldSize = mWorld.SizeInChunks * Chunk.WIDTH;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!InBounds(x, Math.Clamp(y, 1, Chunk.HEIGHT - 2), z, worldSize))
                        continue;

                    BlockType bt = mWorld.GetBlock(x, y, z);
                    if (bt == BlockType.Air)
                        continue;

                    if (!BlockRegistry.IsSolid(bt))
                        continue;

                    if (Aabb.BlockAabb(x, y, z).Intersects(box))
                        return false;
                }
            }
        }

        return true;
    }

    private Entity CreateHostile(Vector3 pos)
    {
        return mRandom.Next(4) switch
        {
            0 => new Skeleton(pos),
            1 => new Stalker(pos),
            2 => new Spider(pos),
            _ => new Zombie(pos),
        };
    }

    private Entity CreatePassive(Vector3 pos) =>
        mRandom.Next(2) == 0 ? new Pig(pos) : new Sheep(pos);
    
    public int DebugSpawnHostilesNow(int candidateCount, bool ignoreCap)
    {
        int hostileCount = 0;
        foreach (var e in mWorld.Entities)
        {
            if (!e.IsAlive) continue;
            if (e is Zombie or Skeleton or Stalker or Spider) hostileCount++;
        }

        int hostileCap = GetHostileCap();
        if (!ignoreCap && hostileCount >= hostileCap)
            return 0;

        Vector3 playerPos = Game.Instance.GetPlayer.Position;

        float sunAngle = Game.Instance.TimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0f, 1f);
        int skylightSubtracted = (int)MathHelper.Lerp(15f, 4f, sunlightLevel);

        int spawned = 0;

        for (int i = 0; i < candidateCount; i++)
        {
            if (!ignoreCap && hostileCount >= hostileCap)
                break;

            if (TrySpawnCategory(
                    playerPos,
                    skylightSubtracted,
                    biasedLowY: true,
                    spawnHostile: true))
            {
                spawned++;
                hostileCount++;
            }
        }

        return spawned;
    }
}