// Skeleton-specific AI shoots arrows at the player within 10 blocks. | DA | 3/2/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

public class SkeletonAi : HostileEntityAi
{
    private const int SHOOT_COOLDOWN = 30;
    private const float ARROW_SPREAD = 0.09f;

    protected override float AttackRange => 10f;

    private int mShootCooldown;
    private readonly Skeleton mSkeleton;

    public SkeletonAi(Skeleton skeleton) : base(skeleton)
    {
        mSkeleton = skeleton;
    }

    protected override void OnAttackEntity(World world, float dist)
    {
        if (mShootCooldown > 0)
        {
            mShootCooldown--;
            return;
        }

        Vector3 playerPos = Game.Instance.GetPlayer.Position;
        Vector3 origin = mSkeleton.Position + new Vector3(0f, mSkeleton.Height * 0.75f, 0f);

        float dx = playerPos.X - origin.X;
        float dy = playerPos.Y + 0.9f - origin.Y - 0.2f; // aim at player chest
        float dz = playerPos.Z - origin.Z;
        float horizDist = MathF.Sqrt(dx * dx + dz * dz) * 0.2f; // arc compensation proportional to range

        Vector3 dir = new Vector3(dx, dy + horizDist, dz).Normalized();
        world.AddEntity(new ArrowEntity(mSkeleton, origin, dir, ARROW_SPREAD));
        Game.Instance.AudioManager.PlayAudio("Resources/Audio/Bow/BowRelease.ogg", Game.Instance.AudioManager.SfxVol);

        mShootCooldown = SHOOT_COOLDOWN;
    }
}