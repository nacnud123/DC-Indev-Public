// Sheep entity with rigid-body part animation (body, head, legs) | DA | 2/14/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

public class Sheep : Entity
{
    private const string BODY_MODEL = "Resources/Entities/Sheep/SheepBody/SheepBody.obj";
    private const string BODY_TEXTURE = "Resources/Entities/Sheep/SheepBody/texture.png";
    private const string HEAD_MODEL = "Resources/Entities/Sheep/SheepHead/SheepHead.obj";
    private const string HEAD_TEXTURE = "Resources/Entities/Sheep/SheepHead/Head.png";
    private const string LEG_MODEL = "Resources/Entities/Sheep/SheepLeg/SheepLeg.obj";
    private const string LEG_TEXTURE = "Resources/Entities/Sheep/SheepLeg/Leg.png";

    private static readonly Vector3 BodyOffset = new(-0.0625f, .125f, 0f);
    private static readonly Vector3 HeadOffset = new(0.1875f, 0.25f, 0.03125f);
    private static readonly Vector3 FrontRightLegOffset = new(0.0625f, 0, -0.0625f);
    private static readonly Vector3 FrontLeftLegOffset = new(0.0625f, 0, 0.0625f);
    private static readonly Vector3 BackRightLegOffset = new(-0.125f, 0, -0.0625f);
    private static readonly Vector3 BackLeftLegOffset = new(-0.125f, 0, 0.0625f);

    private static readonly Vector3 HeadPivot = new(-0.0625f, 0.0625f, 0f);
    private static readonly Vector3 LegPivot = new(0.03125f, 0.1875f, 0.03125f);

    private const float MAX_LEG_SWING = MathF.PI / 4f;
    private const float WALK_ANIM_SPEED = 6f;
    private const float SWING_DECAY = 0.75f;
    private const float HEAD_LOOK_RANGE = 8f;
    private const float HEAD_TURN_SPEED = 0.15f;
    private const float MAX_HEAD_YAW = MathF.PI / 2f;
    private const float MAX_HEAD_PITCH = MathF.PI / 6f;

    private readonly EntityModel mBodyModel;
    private readonly EntityModel mHeadModel;
    private readonly EntityModel mLegModel;

    private float mWalkPhase;
    private float mLegSwing;
    private float mHeadYaw;
    private float mHeadPitch;
    private float mIdleSoundTimer;

    public override float Width { get => 0.9f; set { } }
    public override float Height { get => 0.9f; set { } }
    public override float Scale { get => 4f; set { } }
    public override float WalkSpeed { get => 2f; set { } }
    public override int Health { get; set; } = 20;


    // Init sheep / load models in
    public Sheep(Vector3 position)
    {
        Position = position;
        InitShader();
        mBodyModel = EntityModel.Load(BODY_MODEL, BODY_TEXTURE);
        mHeadModel = EntityModel.Load(HEAD_MODEL, HEAD_TEXTURE);
        mLegModel = EntityModel.Load(LEG_MODEL, LEG_TEXTURE);
        CurrentAI = new PassiveEntityAi(this);
        mIdleSoundTimer = 5f + (float)Game.Instance.GameRandom.NextDouble() * 10f;
    }

    // Tick, run the AI, update the animation, and play idle sound if it is time
    public override void Tick(World world)
    {
        base.Tick(world);
        CurrentAI.Tick(world);
        UpdateAnimation();

        mIdleSoundTimer -= TickSystem.TICK_DURATION;
        if (mIdleSoundTimer <= 0f)
        {
            mIdleSoundTimer = 5f + (float)Game.Instance.GameRandom.NextDouble() * 10f;
            int idx = Game.Instance.GameRandom.Next(1, 4);
            Game.Instance.AudioManager.PlayAudio(
                $"Resources/Audio/Entities/Sheep/SheepIdle{idx}.ogg",
                Proximity((Game.Instance.GetPlayer.Position - this.Position).Length, 20f, Game.Instance.AudioManager.SfxVol),
                false);
        }
    }

    // What to do when the sheep gets hit
    public override void TakeDamage(int amount)
    {
        base.TakeDamage(amount);
        
        Game.Instance.AudioManager.PlayAudio(
            "Resources/Audio/Entities/Sheep/SheepDie.ogg",
            Proximity((Game.Instance.GetPlayer.Position - this.Position).Length, 20f, Game.Instance.AudioManager.SfxVol),
            false);
    }

    // Play the walking animation if the sheep is moving. Basically swings the legs back and forth. Also, if the player gets close enough the sheep will look at the player.
    private void UpdateAnimation()
    {
        float dt = TickSystem.TICK_DURATION;
        float hSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);

        if (hSpeed > 0.01f)
        {
            mWalkPhase += hSpeed * WALK_ANIM_SPEED * dt;
            mLegSwing = 1f;
        }
        else
        {
            mLegSwing *= SWING_DECAY;
            if (mLegSwing < 0.01f) mLegSwing = 0f;
        }

        if (hSpeed < 0.01f && CurrentAI is not { IsFleeing: true })
        {
            Vector3 toPlayer = Game.Instance.GetPlayer.Position - Position;
            float distSq = toPlayer.LengthSquared;

            if (distSq < HEAD_LOOK_RANGE * HEAD_LOOK_RANGE && distSq > 0.01f)
            {
                float relativeYaw = MathF.Atan2(toPlayer.X, toPlayer.Z) - MathF.PI / 2f - Yaw;
                while (relativeYaw > MathF.PI) relativeYaw -= MathF.PI * 2f;
                while (relativeYaw < -MathF.PI) relativeYaw += MathF.PI * 2f;
                relativeYaw = Math.Clamp(relativeYaw, -MAX_HEAD_YAW, MAX_HEAD_YAW);

                float dy = (Game.Instance.GetPlayer.Position.Y + 0.9f) - (Position.Y + HeadOffset.Y * Scale);
                float dxz = MathF.Sqrt(toPlayer.X * toPlayer.X + toPlayer.Z * toPlayer.Z);
                float targetPitch = Math.Clamp(MathF.Atan2(dy, dxz), -MAX_HEAD_PITCH, MAX_HEAD_PITCH);

                mHeadYaw += (relativeYaw - mHeadYaw) * HEAD_TURN_SPEED;
                mHeadPitch += (targetPitch - mHeadPitch) * HEAD_TURN_SPEED;
                return;
            }
        }

        mHeadYaw *= 1f - HEAD_TURN_SPEED;
        mHeadPitch *= 1f - HEAD_TURN_SPEED;
    }

    // Draw the sheep. Duplicate the legs and draw four of them with different offsets. Leg's have own draw function
    protected override void DrawModel(Matrix4 view, Matrix4 projection)
    {
        Matrix4 entityBase = Matrix4.CreateScale(Scale) * Matrix4.CreateRotationY(Yaw) * Matrix4.CreateTranslation(Position);
        Matrix4 vp = view * projection;

        DrawPart(mBodyModel, Matrix4.CreateTranslation(BodyOffset) * entityBase * vp);

        Matrix4 headLocal = Matrix4.CreateTranslation(-HeadPivot)
                            * Matrix4.CreateRotationZ(mHeadPitch) * Matrix4.CreateRotationY(mHeadYaw)
                            * Matrix4.CreateTranslation(HeadPivot + HeadOffset);
        DrawPart(mHeadModel, headLocal * entityBase * vp);

        float swing1 = MathF.Sin(mWalkPhase) * MAX_LEG_SWING * mLegSwing;
        float swing2 = MathF.Sin(mWalkPhase + MathF.PI) * MAX_LEG_SWING * mLegSwing;
        DrawLeg(swing1, FrontLeftLegOffset, entityBase, vp);
        DrawLeg(swing1, BackRightLegOffset, entityBase, vp);
        DrawLeg(swing2, FrontRightLegOffset, entityBase, vp);
        DrawLeg(swing2, BackLeftLegOffset, entityBase, vp);
    }

    // Draw the leg at the offset.
    private void DrawLeg(float swingAngle, Vector3 offset, Matrix4 entityBase, Matrix4 vp)
    {
        Matrix4 legLocal = Matrix4.CreateTranslation(-LegPivot)
                           * Matrix4.CreateRotationZ(swingAngle)
                           * Matrix4.CreateTranslation(LegPivot + offset);
        DrawPart(mLegModel, legLocal * entityBase * vp);
    }
}
