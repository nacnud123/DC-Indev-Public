// Zombie AI chases player and deals melee damage when in range. | DA | 3/2/26
// Attack range 2.5 blocks, 20-tick cooldown (1 second), 3 damage per hit.

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

public class ZombieAi : HostileEntityAi
{
    private const int ATTACK_COOLDOWN = 20;
    private const int ATTACK_DAMAGE = 3;

    protected override float AttackRange => 2.5f;

    private int mAttackCooldown;

    public ZombieAi(Entity entity) : base(entity)
    {
    }

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
        Vector3 knockDir = delta.LengthSquared > 0.01f ? delta.Normalized() : Vector3.UnitZ;
        player.Velocity += new Vector3(knockDir.X, 0.6f, knockDir.Z) * 12f;

        mAttackCooldown = ATTACK_COOLDOWN;
    }
}