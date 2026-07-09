// Sheep entity with rigid-body part animation (body, head, legs) | DA | 2/14/26


using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

/// <summary>
/// Passive mob: a sheep. Wanders via <see cref="PassiveEntityAi"/> and procedurally animates its body/head/legs like <see cref="Pig"/>. Has a wool coat drawn as a separate overlay mesh; the first hit "shears" it (drops white wool, no damage/death) and swaps to the woolless body model, subsequent hits deal real damage and can kill it (dropping more wool if not already sheared).
/// </summary>
public class Sheep : Entity
{
    // Model/texture asset paths for each independently-drawn body part, plus a separate wool overlay mesh.
    private const string BODY_MODEL = "Resources/Entities/Sheep/SheepBody/SheepBody.obj";
    private const string BODY_TEXTURE = "Resources/Entities/Sheep/SheepBody/texture.png";
    private const string BODY_WOOL_MODEL = "Resources/Entities/Sheep/SheepBodyWool/SheepBodyWool.obj";
    private const string BODY_WOOL_TEXTURE = "Resources/Entities/Sheep/SheepBodyWool/SheepBodyWool.png";
    private const string HEAD_MODEL = "Resources/Entities/Sheep/SheepHead/SheepHead.obj";
    private const string HEAD_TEXTURE = "Resources/Entities/Sheep/SheepHead/Head.png";
    private const string LEG_MODEL = "Resources/Entities/Sheep/SheepLeg/SheepLeg.obj";
    private const string LEG_TEXTURE = "Resources/Entities/Sheep/SheepLeg/Leg.png";

    // Local-space offsets (in model units, pre-Scale) placing each part relative to the entity origin.
    private static readonly Vector3 BodyOffset = new(0f, 0.125f, 0.032f);
    private static readonly Vector3 HeadOffset = new(0.1875f, 0.25f, 0.03125f);
    private static readonly Vector3 FrontRightLegOffset = new(0.0625f, 0, -0.0625f);
    private static readonly Vector3 FrontLeftLegOffset = new(0.0625f, 0, 0.0625f);
    private static readonly Vector3 BackRightLegOffset = new(-0.125f, 0, -0.0625f);
    private static readonly Vector3 BackLeftLegOffset = new(-0.125f, 0, 0.0625f);

    // Pivot points (relative to each part's own origin) that rotations are applied around, so the head/legs swing from their joint rather than their mesh center.
    private static readonly Vector3 HeadPivot = new(-0.0625f, 0.0625f, 0f);
    private static readonly Vector3 LegPivot = new(0.03125f, 0.1875f, 0.03125f);

    private const float MAX_LEG_SWING = MathF.PI / 4f;   // radians (45 deg) max leg swing amplitude
    private const float WALK_ANIM_SPEED = 6f;              // how fast walk-cycle phase advances per unit horizontal speed
    private const float SWING_DECAY = 0.75f;               // per-tick multiplier that eases leg swing back to idle when stopped
    private const float HEAD_LOOK_RANGE = 8f;               // blocks; player must be within this radius for the sheep to look at them
    private const float HEAD_TURN_SPEED = 0.15f;            // lerp factor per tick toward the target head yaw/pitch
    private const float MAX_HEAD_YAW = MathF.PI / 2f;      // radians (90 deg) clamp so the head can't twist unnaturally
    private const float MAX_HEAD_PITCH = MathF.PI / 6f;    // radians (30 deg) clamp on up/down head tilt

    private readonly EntityModel mBodyModel;      // woolless body, shown once sheared
    private readonly EntityModel mBodyWoolModel;  // fluffy wool overlay body, shown while unsheared
    private readonly EntityModel mHeadModel;
    private readonly EntityModel mLegModel; // single leg mesh reused for all four legs via DrawLeg

    private float mWalkPhase;   // radians; advances while moving, drives the sinusoidal leg-swing animation
    private float mLegSwing;    // 0..1 blend factor; 1 while walking, decays toward 0 (SWING_DECAY) when idle
    private float mHeadYaw;     // current smoothed head yaw offset (radians) relative to body facing
    private float mHeadPitch;   // current smoothed head pitch offset (radians)
    private float mIdleSoundTimer; // seconds until next random idle bleat; re-randomized each time it fires

    // Fixed stats for sheep: collision box dims, render scale, and movement speed are constants (setters are no-ops because the base Entity exposes these as settable, but Sheep does not allow per-instance variation).
    public override float Width
    {
        get => 0.9f;
        set { }
    }

    public override float Height
    {
        get => 0.9f;
        set { }
    }

    public override float Scale
    {
        get => 4f;
        set { }
    }

    public override float WalkSpeed
    {
        get => 2f;
        set { }
    }

    public override int Health { get; set; } = 20;
    /// <summary>True once the sheep's wool has been taken (via TakeDamage's shear-on-first-hit logic); switches to the woolless model and prevents further wool drops.</summary>
    public bool IsSheared { get; set; }

    /// <summary>Spawns a sheep at the given world-space position, loads its part/wool models, and attaches passive wander AI.</summary>
    public Sheep(Vector3 position)
    {
        Position = position;
        InitShader();
        mBodyModel = EntityModel.Load(BODY_MODEL, BODY_TEXTURE);
        mBodyWoolModel = EntityModel.Load(BODY_WOOL_MODEL, BODY_WOOL_TEXTURE);
        mHeadModel = EntityModel.Load(HEAD_MODEL, HEAD_TEXTURE);
        mLegModel = EntityModel.Load(LEG_MODEL, LEG_TEXTURE);
        CurrentAI = new PassiveEntityAi(this);
        // Random 5-15s delay before the first idle bleat so multiple sheep don't sync up.
        mIdleSoundTimer = 5f + (float)Game.Instance.GameRandom.NextDouble() * 10f;
    }

    /// <summary>Per-tick update: runs base entity physics, ticks the wander AI, advances part animation, and fires idle sounds on a random timer.</summary>
    public override void Tick(World world)
    {
        base.Tick(world);
        CurrentAI.Tick(world);
        UpdateAnimation();

        mIdleSoundTimer -= TickSystem.TICK_DURATION; // TICK_DURATION is in seconds, so this timer counts down in seconds
        if (mIdleSoundTimer <= 0f)
        {
            mIdleSoundTimer = 5f + (float)Game.Instance.GameRandom.NextDouble() * 10f;
            int idx = Game.Instance.GameRandom.Next(1, 4); // picks one of SheepIdle1..3.ogg

            // Proximity attenuates volume based on distance to the player, falling off to 0 at 20 blocks.
            Game.Instance.AudioManager.PlayAudio($"Resources/Audio/Entities/Sheep/SheepIdle{idx}.ogg", Proximity((Game.Instance.GetPlayer.Position - this.Position).Length(), 20f, Game.Instance.AudioManager.SfxVol), false);
        }
    }

    /// <summary>
    /// Overrides the base damage/death handling to implement shearing: the very first hit a sheep takes (while unsheared) consumes no health at all and instead drops 1-3 white wool blocks and flips <see cref="IsSheared"/> - this doubles as "punch to shear" since there's no shears item gating it here. Every subsequent hit is a normal damage hit that can kill the sheep, and if it dies without ever having been sheared it drops one more batch of wool on death.
    /// </summary>
    public override void TakeDamage(int amount)
    {
        if (!IsSheared)
        {
            IsSheared = true;

            int count = Game.Instance.GameRandom.Next(1, 4); // 1-3
            var drop = new DroppedItemEntity(Position, ItemStack.FromBlock(BlockType.White, count), Game.Instance.WorldTexture);

            Game.Instance.GetWorld.AddEntity(drop);
            return; // first hit only shears - no damage/death check this call
        }

        base.TakeDamage(amount);

        Game.Instance.AudioManager.PlayAudio("Resources/Audio/Entities/Sheep/SheepDie.ogg", Proximity((Game.Instance.GetPlayer.Position - this.Position).Length(), 20f, Game.Instance.AudioManager.SfxVol), false);

        // Only drop wool on death if it was never sheared while alive (sheared sheep already gave up their wool). NOTE: because of the early-return above, this method only ever reaches here when IsSheared is already true, so in practice this branch is currently unreachable - kept as a defensive check.
        if (!IsAlive && !IsSheared)
        {
            int count = Game.Instance.GameRandom.Next(1, 4);

            var drop = new DroppedItemEntity(Position, ItemStack.FromBlock(BlockType.White, count), Game.Instance.WorldTexture);

            Game.Instance.GetWorld.AddEntity(drop);
        }
    }

    // Play the walking animation if the sheep is moving. Basically swings the legs back and forth. Also, if the player gets close enough the sheep will look at the player. This is purely cosmetic per-tick animation state - it does not affect physics or AI decisions.
    private void UpdateAnimation()
    {
        float dt = TickSystem.TICK_DURATION;
        float hSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);

        if (hSpeed > 0.01f)
        {
            // Walk-cycle phase advances proportional to speed so faster movement animates faster (not time-based alone).
            mWalkPhase += hSpeed * WALK_ANIM_SPEED * dt;
            mLegSwing = 1f;
        }
        else
        {
            // Exponential ease-out back to a neutral stance once the sheep stops moving.
            mLegSwing *= SWING_DECAY;

            if (mLegSwing < 0.01f)
                mLegSwing = 0f;
        }

        // Only look at the player while standing still (walking animation takes priority over head-tracking).
        if (hSpeed < 0.01f)
        {
            Vector3 toPlayer = Game.Instance.GetPlayer.Position - Position;
            float distSq = toPlayer.LengthSquared(); // squared distance avoids a sqrt for the range check

            if (distSq < HEAD_LOOK_RANGE * HEAD_LOOK_RANGE && distSq > 0.01f)
            {
                // Yaw needed to face the player, converted into the sheep's local space (subtract entity Yaw) and offset by PI/2 because Atan2(X,Z) and the sheep's forward axis are 90 degrees apart.
                float relativeYaw = MathF.Atan2(toPlayer.X, toPlayer.Z) - MathF.PI / 2f - Yaw;

                // Normalize into [-PI, PI] so the shortest turn direction is used.
                while (relativeYaw > MathF.PI)
                    relativeYaw -= MathF.PI * 2f;

                while (relativeYaw < -MathF.PI)
                    relativeYaw += MathF.PI * 2f;

                relativeYaw = Math.Clamp(relativeYaw, -MAX_HEAD_YAW, MAX_HEAD_YAW);

                // 0.9f approximates the player's eye height above their feet position.
                float dy = (Game.Instance.GetPlayer.Position.Y + 0.9f) - (Position.Y + HeadOffset.Y * Scale);
                float dxz = MathF.Sqrt(toPlayer.X * toPlayer.X + toPlayer.Z * toPlayer.Z);
                float targetPitch = Math.Clamp(MathF.Atan2(dy, dxz), -MAX_HEAD_PITCH, MAX_HEAD_PITCH);

                // Exponential smoothing (lerp toward target each tick) rather than snapping instantly.
                mHeadYaw += (relativeYaw - mHeadYaw) * HEAD_TURN_SPEED;
                mHeadPitch += (targetPitch - mHeadPitch) * HEAD_TURN_SPEED;
                return;
            }
        }

        // No target to look at (moving, or player out of range): relax the head back toward neutral.
        mHeadYaw *= 1f - HEAD_TURN_SPEED;
        mHeadPitch *= 1f - HEAD_TURN_SPEED;
    }

    /// <summary>
    /// Renders the sheep: draws either the wool overlay body (scaled 1.1x to sit visibly over the base body mesh) or the plain body depending on <see cref="IsSheared"/>, then the head with yaw/pitch applied at the neck pivot, then four legs swung in diagonal pairs like a real quadruped gait.
    /// </summary>
    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 entityBase = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateTranslation(Position);
        Matrix4x4 vp = view * projection;

        if (IsSheared)
        {
            DrawPart(mBodyModel, Matrix4x4.CreateTranslation(BodyOffset) * entityBase * vp);
        }
        else
        {
            Matrix4x4 woolTransform = Matrix4x4.CreateScale(1.1f) * Matrix4x4.CreateTranslation(BodyOffset);
            DrawPart(mBodyWoolModel, woolTransform * entityBase * vp);
        }

        // Rotate the head about its pivot (translate to origin, rotate, translate back + offset) rather than the model's own origin, so it swivels naturally from the neck joint.
        Matrix4x4 headLocal = Matrix4x4.CreateTranslation(-HeadPivot) * Matrix4x4.CreateRotationZ(mHeadPitch) * Matrix4x4.CreateRotationY(mHeadYaw) * Matrix4x4.CreateTranslation(HeadPivot + HeadOffset);

        DrawPart(mHeadModel, headLocal * entityBase * vp);

        // Diagonal gait: front-left+back-right swing together (swing1), front-right+back-left swing in antiphase (swing2, offset by PI), mimicking a real quadruped's walk cycle.
        float swing1 = MathF.Sin(mWalkPhase) * MAX_LEG_SWING * mLegSwing;
        float swing2 = MathF.Sin(mWalkPhase + MathF.PI) * MAX_LEG_SWING * mLegSwing;

        DrawLeg(swing1, FrontLeftLegOffset, entityBase, vp);
        DrawLeg(swing1, BackRightLegOffset, entityBase, vp);
        DrawLeg(swing2, FrontRightLegOffset, entityBase, vp);
        DrawLeg(swing2, BackLeftLegOffset, entityBase, vp);
    }

    /// <summary>Fall damage: no damage for falls of 3 blocks or less, then roughly 1 damage per block beyond that.</summary>
    protected override void Fall(World world, float dist)
    {
        int damage = (int)MathF.Ceiling(dist - 3f);

        if (damage > 0)
            TakeDamage(damage);
    }

    // Draws a single leg mesh rotated about its knee/hip pivot by swingAngle, then offset to one of the four leg positions.
    private void DrawLeg(float swingAngle, Vector3 offset, Matrix4x4 entityBase, Matrix4x4 vp)
    {
        Matrix4x4 legLocal = Matrix4x4.CreateTranslation(-LegPivot) * Matrix4x4.CreateRotationZ(swingAngle) * Matrix4x4.CreateTranslation(LegPivot + offset);
        DrawPart(mLegModel, legLocal * entityBase * vp);
    }


}