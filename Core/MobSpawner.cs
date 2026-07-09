// Mob spawner adapted from Minecraft Indev. | DA | 3/10/26


using VoxelEngine.GameEntity;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Core;

/// <summary>
/// Ticks once per game tick (20/s, driven by <see cref="TickSystem"/>) and probabilistically spawns hostile and passive mobs into the world, mirroring Minecraft Indev-era spawning rules: random world-column candidate positions are picked, then "scattered" via a small random walk, filtered by distance-from-player, block placement legality, and light level, and finally checked for physical clearance before an entity is actually added to the world. Spawn counts are capped relative to world size so mob density scales with world footprint rather than growing unbounded.
/// </summary>
public class MobSpawner
{
    // Squared distance (in blocks) mobs must spawn away from the player, avoiding mobs popping into existence right next to / inside the player's view. Squared so we can compare against dx*dx+dy*dy+dz*dz without an expensive sqrt.
    private const int MIN_PLAYER_DIST_SQ = 32 * 32;
    // How many random spawn attempts are made per tick for hostiles vs. passives. Hostiles get more attempts since they're rarer to find valid (dark) spots for.
    private const int HOSTILE_LOOPS_PER_TICK = 4;
    private const int PASSIVE_LOOPS_PER_TICK = 1;
    // Number of "outer" candidate positions tried per loop iteration...
    private const int OUTER_SCATTER = 2;
    // ...and number of random-walk "inner" steps taken from each outer candidate, looking for a valid spot nearby. This outer/inner scatter is the classic Minecraft Indev spawn-clustering algorithm: it tends to place several mobs near each other rather than uniformly at random, producing natural-looking mob packs/groups.
    private const int INNER_SCATTER = 3;

    private readonly World mWorld;
    private readonly Random mRandom;

    public MobSpawner(World world, Random random)
    {
        mWorld = world;
        mRandom = random;
    }

    /// <summary>
    /// Called once per simulation tick. Counts currently-alive hostile/passive mobs, and if either is below its world-size-scaled cap, attempts a batch of random spawns for that category. Bails out early entirely if both categories are already at or above cap.
    /// </summary>
    public void Tick()
    {
        int hostileCount = 0;
        int passiveCount = 0;

        foreach (var e in mWorld.Entities)
        {
            if (!e.IsAlive)
                continue;

            if (e is Zombie or Skeleton or Stalker or Spider)
            {
                hostileCount++;
            }
            else if (e is Pig or Sheep)
            {
                passiveCount++;
            }
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
                // On the last attempt of the batch, pick the Y coordinate uniformly across the full height range instead of biased toward the surface (see the ay calculation in TrySpawnHostile) — this gives cave-dwelling mobs a periodic chance to spawn deep underground even though most attempts favor near-surface elevations.
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

    /// <summary>
    /// Attempts a single hostile-mob spawn using the outer/inner scatter algorithm. Picks a random world-column (x,z) and a biased Y (see below), then randomly walks nearby looking for a tile that passes distance, block-placement, and darkness checks. Returns true and adds the mob to the world on success.
    /// </summary>
    private bool TrySpawnHostile(Vector3 playerPos, int skylightSubtracted, bool uniformY = false)
    {
        int worldSize = mWorld.SizeInChunks * Chunk.WIDTH;

        if (worldSize <= 0)
            return false;

        int ax = mRandom.Next(worldSize);
        int az = mRandom.Next(worldSize);
        // Non-uniform Y bias: taking the min of two uniform random draws skews the distribution toward 0, so most hostile spawn candidates land near the bottom of the height range unless uniformY overrides this (see Tick()). This roughly approximates "mobs prefer to spawn low/underground" without a full cave-surface classification pass.
        int ay = uniformY
            ? mRandom.Next(1, Chunk.HEIGHT - 2)
            : (int)(Math.Min(mRandom.NextDouble(), mRandom.NextDouble()) * (Chunk.HEIGHT - 3)) + 1;

        // 1-in-5 chance to abandon this candidate entirely before even scattering, further throttling hostile spawn rate beyond just the cap check.
        if (mRandom.Next(5) == 4)
            return false;

        for (int outer = 0; outer < OUTER_SCATTER; outer++)
        {
            int x = ax, z = az;

            for (int inner = 0; inner < INNER_SCATTER; inner++)
            {
                // Random walk step in [-5, 5] on each axis (difference of two Next(6) draws), clustering successive inner attempts around the outer candidate rather than jumping to a totally new column each time.
                x += mRandom.Next(6) - mRandom.Next(6);
                z += mRandom.Next(6) - mRandom.Next(6);

                if (!InBounds(x, ay, z, worldSize))
                    continue;

                // Squared distance check against the player (block centers, +0.5 to sample the middle of the block rather than its corner).
                float dx = (x + 0.5f) - playerPos.X;
                float dy = ay - playerPos.Y;
                float dz = (z + 0.5f) - playerPos.Z;
                if (dx * dx + dy * dy + dz * dz <= MIN_PLAYER_DIST_SQ)
                    continue;

                if (!MeetsBlockPlacementRules(x, ay, z))
                    continue;

                // Hostiles need darkness to spawn: effectiveLight must beat a random roll in [0,7], so light level 0 always passes and higher light levels are increasingly likely to block the spawn (probabilistic rather than a hard light threshold, matching Indev/early-Minecraft behavior).
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

    /// <summary>
    /// Passive-mob counterpart to <see cref="TrySpawnHostile"/>. Uses a uniform Y range (passive mobs don't prefer depth) and requires bright light (effectiveLight &gt; 8) rather than darkness, so they spawn in daylight on the surface instead of in caves.
    /// </summary>
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

    /// <summary>
    /// Derives how much daylight's contribution to sky light should be reduced this tick, based on time of day. mTimeOfDay's sine wave peaks at noon (bright, subtract only 4) and is near zero/negative at night (dark, subtract the full 15 — i.e. skylight contributes nothing), producing a smooth day/night transition in effective light rather than an instant day/night cutoff. Range is [4,15], matching sky light's 0-15 value range.
    /// </summary>
    private int GetSkylightSubtracted()
    {
        float sunAngle = Game.Instance.TimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0f, 1f);

        return (int)(15f + (4f - 15f) * sunlightLevel);
    }

    /// <summary>
    /// Combines block light (torches etc., unaffected by time of day) and sky light (reduced by <see cref="GetSkylightSubtracted"/> to account for night darkness) into a single 0-15 "effective" light value used by spawn darkness/brightness checks.
    /// </summary>
    private int GetEffectiveLight(int x, int y, int z, int skylightSubtracted)
    {
        int skyLight = mWorld.GetSkyLight(x, y, z);
        int blockLight = mWorld.GetBlockLight(x, y, z);

        return Math.Max(0, Math.Max(blockLight, skyLight - skylightSubtracted));
    }

    /// <summary>
    /// World-size-scaled maximum number of simultaneously alive hostile mobs. Derived from a per-64x64x64-volume mob density (20 mobs) scaled by actual world volume, halved to keep hostile density lower than the density constant alone implies.
    /// </summary>
    private int GetHostileCap()
    {
        int width = mWorld.SizeInChunks * Chunk.WIDTH;
        int cap = (int)Math.Floor(width * (double)width * Chunk.HEIGHT * 20.0 / (64.0 * 64.0 * 64.0) / 2.0);

        return Math.Max(0, cap);
    }

    /// <summary>
    /// World-size-scaled maximum number of simultaneously alive passive mobs. Scales with world footprint area (not volume, unlike hostiles) since passive mobs only spawn near the surface.
    /// </summary>
    private int GetPassiveCap()
    {
        int width = mWorld.SizeInChunks * Chunk.WIDTH;

        return Math.Max(0, (int)Math.Floor(width * (double)width / 4000.0));
    }

    // Checks a block coordinate is within the world's horizontal chunk grid and within the vertical band that's valid for a mob to stand in (excludes the very bottom/top rows so both the mob's feet and a block above it are always addressable).
    private bool InBounds(int x, int y, int z, int worldSize) =>
        x >= 0 && x < worldSize &&
        z >= 0 && z < worldSize &&
        y >= 1 && y < Chunk.HEIGHT - 2;

    /// <summary>
    /// Checks the minimum terrain shape required for a mob to legally occupy (x,y,z): the spawn block and the block above must be empty air (room to stand), the block below must be solid (a floor to stand on), and none of the three may be a liquid (mobs shouldn't spawn floating in / submerged under water or lava via this path).
    /// </summary>
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

    /// <summary>
    /// Final physical-placement check after a candidate position passes the block-level rules: verifies the mob's actual bounding box (which may span multiple blocks, unlike the single-block checks above) doesn't overlap solid terrain or any other living entity, and that its feet aren't in a liquid.
    /// </summary>
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

    /// <summary>
    /// Sweeps every block cell overlapped by the given AABB and returns false if any of them is solid terrain that actually intersects the box (a coarse per-block sweep, not a single point check, since mob bounding boxes can span multiple blocks tall).
    /// </summary>
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

    // Uniformly picks one of the four hostile mob types for a new spawn.
    private Entity CreateHostile(Vector3 pos) =>
        mRandom.Next(4) switch
        {
            0 => new Skeleton(pos),
            1 => new Stalker(pos),
            2 => new Spider(pos),
            _ => new Zombie(pos),
        };

    // Uniformly picks one of the two passive mob types for a new spawn.
    private Entity CreatePassive(Vector3 pos) =>
        mRandom.Next(2) == 0 ? new Pig(pos) : new Sheep(pos);

    /// <summary>
    /// Debug/dev-command entry point (invoked from an in-game debug command, not the normal tick loop) that forces a batch of hostile spawn attempts immediately, either respecting the normal hostile cap or bypassing it entirely via <paramref name="ignoreCap"/>. Returns how many mobs were actually spawned.
    /// </summary>
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