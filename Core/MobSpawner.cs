// Mob spawner adapted from Minecraft Indev. | DA | 3/10/26

using OpenTK.Mathematics;
using VoxelEngine.GameEntity;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Core;

public class MobSpawner
{
    private const int MIN_PLAYER_DIST_SQ = 32 * 32;
    private const int HOSTILE_LOOPS_PER_TICK = 4;
    private const int PASSIVE_LOOPS_PER_TICK = 1;
    private const int OUTER_SCATTER = 2;
    private const int INNER_SCATTER = 3;

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

        if (hostileCount >= hostileCap && passiveCount >= passiveCap)
            return;

        Vector3 playerPos = Game.Instance.GetPlayer.Position;
        int skylightSubtracted = GetSkylightSubtracted();

        if (hostileCount < hostileCap)
        {
            for (int i = 0; i < HOSTILE_LOOPS_PER_TICK && hostileCount < hostileCap; i++)
            {
                bool uniformY = (i == HOSTILE_LOOPS_PER_TICK - 1);
                if (TrySpawnHostile(playerPos, skylightSubtracted, uniformY))
                    hostileCount++;
            }
        }

        if (passiveCount < passiveCap)
        {
            for (int i = 0; i < PASSIVE_LOOPS_PER_TICK && passiveCount < passiveCap; i++)
            {
                if (TrySpawnPassive(playerPos, skylightSubtracted))
                    passiveCount++;
            }
        }
    }

    private bool TrySpawnHostile(Vector3 playerPos, int skylightSubtracted, bool uniformY = false)
    {
        int worldSize = mWorld.SizeInChunks * Chunk.WIDTH;
        if (worldSize <= 0)
            return false;

        int ax = mRandom.Next(worldSize);
        int az = mRandom.Next(worldSize);
        int ay = uniformY
            ? mRandom.Next(1, Chunk.HEIGHT - 2)
            : (int)(Math.Min(mRandom.NextDouble(), mRandom.NextDouble()) * (Chunk.HEIGHT - 3)) + 1;

        if (mRandom.Next(5) == 4)
            return false;

        for (int outer = 0; outer < OUTER_SCATTER; outer++)
        {
            int x = ax, z = az;

            for (int inner = 0; inner < INNER_SCATTER; inner++)
            {
                x += mRandom.Next(6) - mRandom.Next(6);
                z += mRandom.Next(6) - mRandom.Next(6);
                if (!InBounds(x, ay, z, worldSize))
                    continue;

                float dx = (x + 0.5f) - playerPos.X;
                float dy = ay - playerPos.Y;
                float dz = (z + 0.5f) - playerPos.Z;
                if (dx * dx + dy * dy + dz * dz <= MIN_PLAYER_DIST_SQ)
                    continue;

                if (!MeetsBlockPlacementRules(x, ay, z))
                    continue;

                int effectiveLight = GetEffectiveLight(x, ay, z, skylightSubtracted);
                if (effectiveLight > mRandom.Next(8))
                    continue;

                Entity mob = CreateHostile(new Vector3(x + 0.5f, ay, z + 0.5f));
                if (!CanSpawnEntityHere(mob))
                    continue;

                mWorld.AddEntity(mob);
                return true;
            }
        }

        return false;
    }

    private bool TrySpawnPassive(Vector3 playerPos, int skylightSubtracted)
    {
        int worldSize = mWorld.SizeInChunks * Chunk.WIDTH;
        if (worldSize <= 0)
            return false;

        int ax = mRandom.Next(worldSize);
        int az = mRandom.Next(worldSize);
        int ay = mRandom.Next(1, Chunk.HEIGHT - 2);

        for (int outer = 0; outer < OUTER_SCATTER; outer++)
        {
            int x = ax, z = az;

            for (int inner = 0; inner < INNER_SCATTER; inner++)
            {
                x += mRandom.Next(6) - mRandom.Next(6);
                z += mRandom.Next(6) - mRandom.Next(6);

                if (!InBounds(x, ay, z, worldSize))
                    continue;

                float dx = (x + 0.5f) - playerPos.X;
                float dy = ay - playerPos.Y;
                float dz = (z + 0.5f) - playerPos.Z;
                if (dx * dx + dy * dy + dz * dz <= MIN_PLAYER_DIST_SQ)
                    continue;

                if (!MeetsBlockPlacementRules(x, ay, z))
                    continue;

                int effectiveLight = GetEffectiveLight(x, ay, z, skylightSubtracted);
                if (effectiveLight <= 8)
                    continue;

                Entity mob = CreatePassive(new Vector3(x + 0.5f, ay, z + 0.5f));
                if (!CanSpawnEntityHere(mob))
                    continue;

                mWorld.AddEntity(mob);
                return true;
            }
        }

        return false;
    }

    private int GetSkylightSubtracted()
    {
        float sunAngle = Game.Instance.TimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0f, 1f);

        return (int)MathHelper.Lerp(15f, 4f, sunlightLevel);
    }

    private int GetEffectiveLight(int x, int y, int z, int skylightSubtracted)
    {
        int skyLight = mWorld.GetSkyLight(x, y, z);
        int blockLight = mWorld.GetBlockLight(x, y, z);

        return Math.Max(0, Math.Max(blockLight, skyLight - skylightSubtracted));
    }

    private int GetHostileCap()
    {
        int width = mWorld.SizeInChunks * Chunk.WIDTH;
        int cap = (int)Math.Floor(width * (double)width * Chunk.HEIGHT * 20.0 / (64.0 * 64.0 * 64.0) / 2.0);

        return Math.Max(0, cap);
    }

    private int GetPassiveCap()
    {
        int width = mWorld.SizeInChunks * Chunk.WIDTH;

        return Math.Max(0, (int)Math.Floor(width * (double)width / 4000.0));
    }

    private bool InBounds(int x, int y, int z, int worldSize) =>
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
            if (!other.IsAlive || ReferenceEquals(other, e))
                continue;

            if (other.GetBoundingBox().Intersects(box))
                return false;
        }

        int fx = (int)MathF.Floor(e.Position.X);
        int fy = (int)MathF.Floor(e.Position.Y);
        int fz = (int)MathF.Floor(e.Position.Z);
        var foot = mWorld.GetBlock(fx, fy, fz);

        return foot != BlockType.Water && foot != BlockType.Lava;
    }

    private bool IsAabbClearOfSolids(Aabb box)
    {
        int worldSize = mWorld.SizeInChunks * Chunk.WIDTH;

        for (int x = (int)MathF.Floor(box.Min.X); x <= (int)MathF.Floor(box.Max.X); x++)
        {
            for (int y = (int)MathF.Floor(box.Min.Y); y <= (int)MathF.Floor(box.Max.Y); y++)
            {
                for (int z = (int)MathF.Floor(box.Min.Z); z <= (int)MathF.Floor(box.Max.Z); z++)
                {
                    if (!InBounds(x, Math.Clamp(y, 1, Chunk.HEIGHT - 2), z, worldSize)) 
                        continue;

                    var bt = mWorld.GetBlock(x, y, z);
                    if (bt == BlockType.Air || !BlockRegistry.IsSolid(bt)) 
                        continue;
                    
                    if (Aabb.BlockAabb(x, y, z).Intersects(box)) 
                        return false;
                }
            }
        }

        return true;
    }

    private Entity CreateHostile(Vector3 pos) =>
        mRandom.Next(4) switch
        {
            0 => new Skeleton(pos),
            1 => new Stalker(pos),
            2 => new Spider(pos),
            _ => new Zombie(pos),
        };

    private Entity CreatePassive(Vector3 pos) =>
        mRandom.Next(2) == 0 ? new Pig(pos) : new Sheep(pos);

    public int DebugSpawnHostilesNow(int candidateCount, bool ignoreCap)
    {
        int hostileCount = mWorld.Entities.Count(e => e.IsAlive && e is Zombie or Skeleton or Stalker or Spider);
        if (!ignoreCap && hostileCount >= GetHostileCap()) 
            return 0;

        Vector3 playerPos = Game.Instance.GetPlayer.Position;
        int skylightSubtracted = GetSkylightSubtracted();
        int spawned = 0;

        for (int i = 0; i < candidateCount; i++)
        {
            if (!ignoreCap && hostileCount >= GetHostileCap()) 
                break;
            
            if (TrySpawnHostile(playerPos, skylightSubtracted, uniformY: false))
            {
                spawned++;
                hostileCount++;
            }
        }

        return spawned;
    }
}