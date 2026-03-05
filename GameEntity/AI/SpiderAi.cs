// Spider AI. Minecraft style: aggros only in dim light, leaps when close, gradually deaggros in bright light. | DA | 3/4/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

public class SpiderAi : HostileEntityAi
{
    private const int ATTACK_COOLDOWN = 20;
    private const int ATTACK_DAMAGE = 2;
    private const float DETECTION_RANGE = 32f;
    private const float BRIGHT_THRESHOLD = 0.5f;
    private const float DEAGGRO_CHANCE = 1f / 100f;
    private const float LEAP_CHANCE = 1f / 10f;
    private const float LEAP_MIN_DIST = 2f;
    private const float LEAP_MAX_DIST = 6f;
    private const float LEAP_HORIZONTAL = 5f;

    protected override float AttackRange => 2.5f;
    protected override float DetectionRange => DETECTION_RANGE;

    private int mAttackCooldown;
    private bool mIsAggroed;
    
    private float mWanderDirX;
    private float mWanderDirZ;
    private int mWanderTimer;

    public SpiderAi(Entity entity) : base(entity)
    {
    }
    
    private float GetBrightness(World world)
    {
        int x = (int)MathF.Floor(ParentEntity.Position.X);
        int y = (int)MathF.Floor(ParentEntity.Position.Y);
        int z = (int)MathF.Floor(ParentEntity.Position.Z);

        float sunAngle = Game.Instance.TimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0f, 1f);
        int skylightSub = (int)MathHelper.Lerp(15f, 4f, sunlightLevel);

        int skyLight = world.GetSkyLight(x, y, z);
        int blockLight = world.GetBlockLight(x, y, z);
        int effective = Math.Max(0, Math.Max(blockLight, skyLight - skylightSub));

        return effective / (float)Chunk.MAX_LIGHT;
    }

    public override void Tick(World world)
    {
        float brightness = GetBrightness(world);

        // Getting hurt always aggros regardless of light level
        if (WasHurt)
            mIsAggroed = true;
        
        if (mIsAggroed && brightness >= BRIGHT_THRESHOLD && Random.NextDouble() < DEAGGRO_CHANCE)
            mIsAggroed = false;
        
        if (!mIsAggroed && brightness < BRIGHT_THRESHOLD)
        {
            float dist = (Game.Instance.GetPlayer.Position - ParentEntity.Position).Length;
            if (dist <= DETECTION_RANGE)
                mIsAggroed = true;
        }

        if (mIsAggroed)
        {
            base.Tick(world);
        }
        else
        {
            WasHurt = false;
            PassiveWander(world);
            FaceMovementDirection();
        }
    }

    protected override void OnAttackEntity(World world, float dist)
    {
        // Leap attack: fires when 2–6 blocks away, 10% chance, only if on the ground
        if (dist >= LEAP_MIN_DIST && dist <= LEAP_MAX_DIST && Random.NextDouble() < LEAP_CHANCE && ParentEntity.IsOnGround)
        {
            Vector3 toPlayer = Game.Instance.GetPlayer.Position - ParentEntity.Position;
            Vector3 dir = new Vector3(toPlayer.X, 0f, toPlayer.Z);
            if (dir.LengthSquared > 0.01f)
                dir = dir.Normalized();

            ParentEntity.Velocity = new Vector3(dir.X * LEAP_HORIZONTAL, Physics.JUMP_VEL, dir.Z * LEAP_HORIZONTAL);
            return; // skip melee this tick
        }

        // Melee attack
        if (mAttackCooldown > 0)
        {
            mAttackCooldown--;
            return;
        }

        var player = Game.Instance.GetPlayer;
        Vector3 delta = player.Position - ParentEntity.Position;
        Vector3 knockDir = delta.LengthSquared > 0.01f ? delta.Normalized() : Vector3.UnitZ;
        player.TakeDamage(ATTACK_DAMAGE);
        player.Velocity += new Vector3(knockDir.X, 0.3f, knockDir.Z) * 5f;

        mAttackCooldown = ATTACK_COOLDOWN;
    }

    private void PassiveWander(World world)
    {
        mWanderTimer--;
        if (mWanderTimer <= 0)
        {
            mWanderTimer = 60 + Random.Next(60);
            if (Random.NextDouble() < 0.4)
            {
                mWanderDirX = 0f;
                mWanderDirZ = 0f;
            }
            else
            {
                float angle = (float)(Random.NextDouble() * MathF.PI * 2f);
                mWanderDirX = MathF.Cos(angle);
                mWanderDirZ = MathF.Sin(angle);
            }
        }

        if (mWanderDirX == 0f && mWanderDirZ == 0f)
        {
            ParentEntity.Velocity = new Vector3(0f, ParentEntity.Velocity.Y, 0f);
            return;
        }

        float velY = ParentEntity.Velocity.Y;
        if (ParentEntity.IsOnGround && ShouldJump(world, mWanderDirX, mWanderDirZ))
            velY = Physics.JUMP_VEL;

        ParentEntity.Velocity = new Vector3(
            mWanderDirX * ParentEntity.WalkSpeed * 0.5f,
            velY,
            mWanderDirZ * ParentEntity.WalkSpeed * 0.5f);
    }
}