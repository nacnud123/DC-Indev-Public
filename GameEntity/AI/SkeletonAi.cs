// Skeleton-specific AI shoots arrows at the player within 10 blocks. | DA | 3/2/26


using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

/// <summary>
/// Hostile AI for the Skeleton mob. Overrides melee attacking with a ranged bow attack: once within AttackRange (10 blocks) and with LOS (handled by the base HostileEntityAi state machine), it periodically fires an arrow at the player instead of closing to melee distance.
/// </summary>
public class SkeletonAi : HostileEntityAi
{
    // Ticks between shots (30 ticks = ~1.5s at 20 TPS).
    private const int SHOOT_COOLDOWN = 30;
    // Random spread applied to the arrow's initial direction, passed to ArrowEntity (radians-ish factor, not degrees).
    private const float ARROW_SPREAD = 0.09f;

    // Skeletons engage/shoot from much farther than the melee-default 2.5 blocks.
    protected override float AttackRange => 10f;

    private int mShootCooldown;
    // Strongly-typed reference to the same entity as ParentEntity, avoiding repeated casts.
    private readonly Skeleton mSkeleton;

    public SkeletonAi(Skeleton skeleton) : base(skeleton)
    {
        mSkeleton = skeleton;
    }

    // Called every tick the skeleton is within AttackRange with LOS (see HostileEntityAi.Tick).
    protected override void OnAttackEntity(World world, float dist)
    {
        if (mShootCooldown > 0)
        {
            mShootCooldown--;
            return;
        }

        Vector3 playerPos = Game.Instance.GetPlayer.Position;
        // Arrows originate from roughly the skeleton's "bow hand" height (75% of its model height).
        Vector3 origin = mSkeleton.Position + new Vector3(0f, mSkeleton.Height * 0.75f, 0f);

        float dx = playerPos.X - origin.X;
        float dy = playerPos.Y + 0.9f - origin.Y - 0.2f; // aim at player chest
        float dz = playerPos.Z - origin.Z;
        // Arrows are not affected by an arc/gravity compensation model here beyond this simple linear fudge: bias the vertical aim upward proportionally to horizontal range so shots at longer distances (which drop more before impact) still tend to land.
        float horizDist = MathF.Sqrt(dx * dx + dz * dz) * 0.2f; // arc compensation proportional to range

        Vector3 dir = Vector3.Normalize(new Vector3(dx, dy + horizDist, dz));
        world.AddEntity(new ArrowEntity(mSkeleton, origin, dir, ARROW_SPREAD));
        Game.Instance.AudioManager.PlayAudio("Resources/Audio/Bow/BowRelease.ogg", Game.Instance.AudioManager.SfxVol);

        mShootCooldown = SHOOT_COOLDOWN;
    }
}