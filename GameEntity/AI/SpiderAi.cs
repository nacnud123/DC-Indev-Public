// Spider AI. Minecraft style: aggros only in dim light, leaps when close, gradually deaggros in bright light. | DA | 3/4/26


using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

/// <summary>
/// Hostile AI for the Spider mob. Unlike a plain HostileEntityAi, spiders have an additional aggro gate layered on top of the base chase/attack state machine: they only engage the player while ambient brightness at their position is below BRIGHT_THRESHOLD (Minecraft-style "spiders are neutral in daylight" behaviour), with a small per-tick chance to deaggro once back in bright light. While not aggroed they run their own lightweight passive wander instead of delegating to base.Tick. Also adds a chance-based leap attack when within a mid-range band of the player.
/// </summary>
public class SpiderAi : HostileEntityAi
{
    // Ticks between melee attacks (20 ticks = ~1s).
    private const int ATTACK_COOLDOWN = 20;
    private const int ATTACK_DAMAGE = 2;
    private const float DETECTION_RANGE = 32f;
    // Brightness (0..1) threshold at/above which the spider is considered to be in "daylight" for aggro purposes.
    private const float BRIGHT_THRESHOLD = 0.5f;
    // Per-tick probability of dropping aggro once aggroed and standing in bright light (1%).
    private const float DEAGGRO_CHANCE = 1f / 100f;
    // Per-attack-tick probability of performing a leap instead of a melee swing, when in range (10%).
    private const float LEAP_CHANCE = 1f / 10f;
    // Leap only triggers within this band of distances (in blocks) - too close and a melee swing suffices, too far and a leap wouldn't close the gap usefully.
    private const float LEAP_MIN_DIST = 2f;
    private const float LEAP_MAX_DIST = 6f;
    // Horizontal speed imparted by a leap; vertical component uses the standard jump velocity.
    private const float LEAP_HORIZONTAL = 5f;

    protected override float AttackRange => 2.5f;
    protected override float DetectionRange => DETECTION_RANGE;

    private int mAttackCooldown;
    // True once the spider has aggroed the player; gates whether base.Tick (chase/attack) or PassiveWander runs each tick.
    private bool mIsAggroed;

    // Cached wander heading/timer for the non-aggroed passive-wander behaviour (mirrors the wander logic in HostileEntityAi.Wander, duplicated here since passive wandering runs independently of the base class's own state machine while not aggroed).
    private float mWanderDirX;
    private float mWanderDirZ;
    private int mWanderTimer;

    public SpiderAi(Entity entity) : base(entity)
    {
    }

    // Computes an effective "how lit is this spot" value in [0,1], accounting for the day/night cycle's effect on skylight (skylight is dimmed at night via skylightSub) as well as any nearby block light (torches, lava, etc). Chunk.MAX_LIGHT is the max raw light level (15), used to normalize into [0,1].
    private float GetBrightness(World world)
    {
        int x = (int)MathF.Floor(ParentEntity.Position.X);
        int y = (int)MathF.Floor(ParentEntity.Position.Y);
        int z = (int)MathF.Floor(ParentEntity.Position.Z);

        // Sun angle derived from time-of-day (0..1 over the full day/night cycle); sin() gives a smooth day/night brightness curve. *2 and clamp compress the curve so full daylight brightness is reached well before solar noon rather than only exactly at noon.
        float sunAngle = Game.Instance.TimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0f, 1f);
        // At night (sunlightLevel=0) skylight is subdued by 15 (i.e. fully negated); at full day (sunlightLevel=1) only subdued by 4, so some skylight attenuation from depth/ overhangs still applies. Interpolates between those two extremes.
        int skylightSub = (int)(15f + (4f - 15f) * sunlightLevel);

        int skyLight = world.GetSkyLight(x, y, z);
        int blockLight = world.GetBlockLight(x, y, z);
        // Effective light is the max of block light and (day-adjusted) skylight - matches how actual rendering/mob-spawn light level is computed elsewhere in the engine.
        int effective = Math.Max(0, Math.Max(blockLight, skyLight - skylightSub));

        return effective / (float)Chunk.MAX_LIGHT;
    }

    public override void Tick(World world)
    {
        float brightness = GetBrightness(world);

        // Getting hurt always aggros regardless of light level
        if (WasHurt)
            mIsAggroed = true;

        // Small per-tick chance to give up aggro once standing in bright light (avoids an abrupt "instantly passive the moment it's bright enough" feel).
        if (mIsAggroed && brightness >= BRIGHT_THRESHOLD && Random.NextDouble() < DEAGGRO_CHANCE)
            mIsAggroed = false;

        // Only re-evaluate aggro-on-sight while dim; a spider standing in daylight won't spontaneously aggro even if the player walks within DETECTION_RANGE.
        if (!mIsAggroed && brightness < BRIGHT_THRESHOLD)
        {
            float dist = (Game.Instance.GetPlayer.Position - ParentEntity.Position).Length();
            if (dist <= DETECTION_RANGE)
                mIsAggroed = true;
        }

        if (mIsAggroed)
        {
            // Delegate to HostileEntityAi's normal chase/attack state machine.
            base.Tick(world);
        }
        else
        {
            // Not aggroed: clear any stale hurt flag (base.Tick never ran to consume it) and run independent passive-wander movement instead.
            WasHurt = false;
            PassiveWander(world);
            FaceMovementDirection();
        }
    }

    // Called every tick the spider is within AttackRange with LOS while aggroed.
    protected override void OnAttackEntity(World world, float dist)
    {
        // Leap attack: fires when 2-6 blocks away, 10% chance, only if on the ground. Checked before the melee cooldown so leaps aren't gated by ATTACK_COOLDOWN.
        if (dist >= LEAP_MIN_DIST && dist <= LEAP_MAX_DIST && Random.NextDouble() < LEAP_CHANCE && ParentEntity.IsOnGround)
        {
            Vector3 toPlayer = Game.Instance.GetPlayer.Position - ParentEntity.Position;
            Vector3 dir = new Vector3(toPlayer.X, 0f, toPlayer.Z);
            if (dir.LengthSquared() > 0.01f)
                dir = Vector3.Normalize(dir);

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
        Vector3 knockDir = delta.LengthSquared() > 0.01f ? Vector3.Normalize(delta) : Vector3.UnitZ;
        player.TakeDamage(ATTACK_DAMAGE);
        player.Velocity += new Vector3(knockDir.X, 0.6f, knockDir.Z) * 10f;

        mAttackCooldown = ATTACK_COOLDOWN;
    }

    // Random-walk wander used while not aggroed; mirrors HostileEntityAi.Wander (new heading every 60-120 ticks, 40% chance to stand still) but runs independently of the base class's own state machine since the spider bypasses base.Tick entirely while passive.
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