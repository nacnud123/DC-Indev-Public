// Zombie AI chases player and deals melee damage when in range. | DA | 3/2/26 Attack range 2.5 blocks, 20-tick cooldown (1 second), 3 damage per hit.


using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

/// <summary>
/// Hostile AI for the Zombie mob. Simplest hostile AI in the game: pure melee, no special-case behaviour beyond the inherited chase/wander state machine from HostileEntityAi - just deals damage and knockback on a cooldown while in attack range.
/// </summary>
public class ZombieAi : HostileEntityAi
{
    // Ticks between attacks (20 ticks = ~1s at 20 TPS).
    private const int ATTACK_COOLDOWN = 20;
    private const int ATTACK_DAMAGE = 3;

    protected override float AttackRange => 2.5f;

    private int mAttackCooldown;

    public ZombieAi(Entity entity) : base(entity)
    {
    }

    // Called every tick the zombie is within AttackRange with LOS (see HostileEntityAi.Tick).
    protected override void OnAttackEntity(World world, float dist)
    {
        if (mAttackCooldown > 0)
        {
            mAttackCooldown--;
            return;
        }

        var player = Game.Instance.GetPlayer;
        player.TakeDamage(ATTACK_DAMAGE);

        // Knock player away from the zombie with a slight upward kick
        Vector3 delta = player.Position - ParentEntity.Position;
        Vector3 knockDir = delta.LengthSquared() > 0.01f ? Vector3.Normalize(delta) : Vector3.UnitZ;
        player.Velocity += new Vector3(knockDir.X, 0.6f, knockDir.Z) * 12f;

        mAttackCooldown = ATTACK_COOLDOWN;
    }
}