// Shared explosion logic used by TntEntity and Stalker so blast radius/damage/knockback rules only live in one place.

using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

/// <summary>
/// Static, stateless implementation of an explosion: destroys blocks within a roughly-spherical radius (weighted by distance vs. block hardness) and applies damage + knockback to nearby entities. Shared by <c>TntEntity</c> and <c>Stalker</c> so the two don't duplicate blast rules.
/// </summary>
public static class ExplosionUtil
{
    // Fuse used when an explosion detonates a neighboring TNT block, so chains go off almost instantly instead of each block re-arming with a full fresh fuse.
    public const float ChainReactionFuse = 4f * TickSystem.TICK_DURATION;

    /// <summary>
    /// Detonates an explosion at <paramref name="center"/>. Iterates every integer block position in a bounding cube of side (radius*2+1), keeps only those within Euclidean distance `radius` (cube-minus-corners approximates a sphere), and breaks each breakable block whose hardness is beaten by the distance-falloff-scaled power (see per-block comment below for the exact formula). Chain-reacts into any TNT caught in the blast. Afterwards applies radial damage and knockback to the player and all other world entities except <paramref name="source"/> (so the exploding entity, e.g. the TNT itself, doesn't hurt/knock back itself).
    /// </summary>
    /// <param name="radius">How many blocks out the blast reaches (both for block destruction and entity effect radius).</param>
    /// <param name="power">Blast "strength", compared against each block's Hardness (after distance falloff) to decide if it breaks, and scaled into entity damage.</param>
    /// <param name="knockbackStrength">Multiplier applied to the outward push velocity given to affected entities.</param>
    /// <param name="source">The entity that caused the explosion (if any), excluded from damage/knockback.</param>
    public static void Trigger(World world, Vector3 center, int radius, float power, float knockbackStrength,
        Entity? source = null)
    {
        Game.Instance?.AudioManager.PlayAudio("Resources/Audio/TNTExpload.ogg", Game.Instance.AudioManager.SfxVol);

        // Round the explosion's position down to whole-number block coordinates so we can loop over neighboring blocks.
        int cx = (int)MathF.Floor(center.X);
        int cy = (int)MathF.Floor(center.Y);
        int cz = (int)MathF.Floor(center.Z);

        // Walk every block inside a cube of side (radius*2+1) centered on the blast, then skip the corners so what's left is roughly a sphere.
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dist > radius)
                        continue;

                    int bx = cx + dx, by = cy + dy, bz = cz + dz;
                    var block = world.GetBlock(bx, by, bz);
                    if (block == BlockType.Air)
                        continue;

                    var blockDef = BlockRegistry.Get(block);
                    if (!blockDef.IsBreakable)
                        continue;

                    // Blocks farther from the center feel a weaker blast. Tough blocks (high Hardness) need a stronger blast to break, so distant/tough blocks survive.
                    float effectivePower = power * (1f - dist / radius);
                    if (effectivePower < blockDef.Hardness)
                        continue;

                    Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(bx, by, bz), block);

                    // If we're about to blow up another TNT block, give it a very short fuse instead of the normal one, so chains of TNT go off almost instantly.
                    if (block == BlockType.TNT)
                        BlockTNT.PendingChainFuse = ChainReactionFuse;

                    world.SetBlock(bx, by, bz, BlockType.Air);
                }
            }
        }

        // Push and damage nearby entities. The blast center is offset to the middle of the block (+0.5 on each axis) so distance checks measure from the block's center, not its corner.
        Vector3 blastCenter = new(cx + 0.5f, cy + 0.5f, cz + 0.5f);
        ApplyToEntity(Game.Instance?.GetPlayer, blastCenter, radius, power, knockbackStrength);

        for (int i = 0; i < world.Entities.Count; i++)
        {
            Entity entity = world.Entities[i];
            if (entity == source)
                continue;

            ApplyToEntity(entity, blastCenter, radius, power, knockbackStrength);
        }
    }

    /// <summary>
    /// Applies blast damage and knockback to a single entity if it's alive and within <paramref name="radius"/> of <paramref name="center"/>. Damage and knockback both scale linearly with `proximity` (1.0 at the blast center, 0.0 at the radius edge); damage is `proximity * power * 20`, an arbitrary tuning constant converting blast power into HP.
    /// </summary>
    private static void ApplyToEntity(Entity? entity, Vector3 center, int radius, float power, float knockbackStrength)
    {
        if (entity is not { IsAlive: true })
            return;

        // Aim at the entity's middle rather than its feet, so short and tall entities react similarly.
        Vector3 entityCenter = entity.Position + new Vector3(0, entity.Height * 0.5f, 0);
        Vector3 delta = entityCenter - center;
        float dist = delta.Length();

        if (dist > radius)
            return;

        // proximity goes from 1 (right at the center) to 0 (right at the edge of the blast).
        float proximity = 1f - dist / radius;
        int damage = (int)(proximity * power * 20f);

        if (damage > 0)
            entity.TakeDamage(damage);

        // Push the entity directly away from the blast center, harder the closer it is.
        Vector3 knockDir = dist > 0.01f ? delta / dist : Vector3.UnitY;
        entity.Velocity += knockDir * proximity * knockbackStrength;
    }
}
