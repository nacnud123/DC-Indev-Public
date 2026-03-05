// Hostile skeleton entity, chases the player and shoots arrows. | DA | 3/2/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

public class Skeleton : Entity
{
    private const string BODY_MODEL = "Resources/Entities/Skeleton/SkeletonBody/SkeletonBody.obj";
    private const string BODY_TEXTURE = "Resources/Entities/Skeleton/SkeletonBody/SkeletonBody.png";
    private const string HEAD_MODEL = "Resources/Entities/Skeleton/SkeletonHead/SkeletonHead.obj";
    private const string HEAD_TEXTURE = "Resources/Entities/Skeleton/SkeletonHead/Head.png";
    private const string LEGARM_MODEL = "Resources/Entities/Skeleton/SkeletonLegArm/SkeletonLegArm.obj";
    private const string LEGARM_TEXTURE = "Resources/Entities/Skeleton/SkeletonLegArm/LegArm.png";

    // Animation
    private const float MAX_LIMB_SWING = MathF.PI / 4f;
    private const float WALK_ANIM_SPEED = 6f;
    private const float SWING_DECAY = 0.75f;

    // Part offsets
    private static readonly Vector3 BodyOffset = new(-.04f, 0.188f, 0f);
    private static readonly Vector3 HeadOffset = new(0f, 0.375f, 0f);
    private static readonly Vector3 LeftLegOff = new(-0.04f, 0f, 0.02f);
    private static readonly Vector3 RightLegOff = new(-0.04f, 0f, -0.075f);
    private static readonly Vector3 LeftArmOff = new(0.202f, 0.187f, -0.126f);
    private static readonly Vector3 RightArmOff = new(0.202f, 0.18f, 0.065f);
    private static readonly Vector3 LegPivot = new(0f, 0.1875f, 0f);
    private static readonly Vector3 ArmPivot = new(0f, 0.1875f, 0f);
    private const float ARM_ANGLE = -MathF.PI / 2f;

    private readonly EntityModel mBodyModel;
    private readonly EntityModel mHeadModel;
    private readonly EntityModel mLegArmModel;

    private float mWalkPhase;
    private float mLimbSwing;

    public override float Width
    {
        get => 0.6f;
        set { }
    }

    public override float Height
    {
        get => 1.8f;
        set { }
    }

    public override float Scale
    {
        get => 4f;
        set { }
    }

    public override float WalkSpeed
    {
        get => 2.2f;
        set { }
    }

    public override int Health { get; set; } = 20;

    public Skeleton(Vector3 position)
    {
        Position = position;
        InitShader();
        mBodyModel = EntityModel.Load(BODY_MODEL, BODY_TEXTURE);
        mHeadModel = EntityModel.Load(HEAD_MODEL, HEAD_TEXTURE);
        mLegArmModel = EntityModel.Load(LEGARM_MODEL, LEGARM_TEXTURE);
        CurrentAI = new SkeletonAi(this);
    }

    public override void Tick(World world)
    {
        base.Tick(world);
        BurnInSunlight(world);
        CurrentAI!.Tick(world);
        UpdateAnimation();
    }

    private void BurnInSunlight(World world)
    {
        float sunAngle = Game.Instance.TimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0f, 1f);
        if (sunlightLevel <= 0f)
            return;

        int hx = (int)MathF.Floor(Position.X);
        int hy = (int)MathF.Floor(Position.Y + Height);
        int hz = (int)MathF.Floor(Position.Z);
        if (world.GetSkyLight(hx, hy, hz) == Chunk.MAX_LIGHT)
            FireTimer = MathF.Max(FireTimer, 2f);
    }

    private void UpdateAnimation()
    {
        float dt = TickSystem.TICK_DURATION;
        float hSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);

        if (hSpeed > 0.01f)
        {
            mWalkPhase += hSpeed * WALK_ANIM_SPEED * dt;
            mLimbSwing = 1f;
        }
        else
        {
            mLimbSwing *= SWING_DECAY;
            if (mLimbSwing < 0.01f) mLimbSwing = 0f;
        }
    }

    protected override void DrawModel(Matrix4 view, Matrix4 projection)
    {
        Matrix4 entityBase = Matrix4.CreateScale(Scale) * Matrix4.CreateRotationY(Yaw) *
                             Matrix4.CreateTranslation(Position);
        Matrix4 vp = view * projection;

        DrawPart(mBodyModel, Matrix4.CreateTranslation(BodyOffset) * entityBase * vp);
        DrawPart(mHeadModel, Matrix4.CreateTranslation(HeadOffset) * entityBase * vp);

        float swing1 = MathF.Sin(mWalkPhase) * MAX_LIMB_SWING * mLimbSwing;
        float swing2 = MathF.Sin(mWalkPhase + MathF.PI) * MAX_LIMB_SWING * mLimbSwing;

        DrawLimb(Matrix4.CreateRotationZ(swing1), LeftLegOff, LegPivot, entityBase, vp);
        DrawLimb(Matrix4.CreateRotationZ(swing2), RightLegOff, LegPivot, entityBase, vp);

        Matrix4 armRot = Matrix4.CreateRotationZ(ARM_ANGLE);
        DrawLimb(armRot, LeftArmOff, ArmPivot, entityBase, vp);
        DrawLimb(armRot, RightArmOff, ArmPivot, entityBase, vp);
    }

    private void DrawLimb(Matrix4 localRot, Vector3 offset, Vector3 pivot, Matrix4 entityBase, Matrix4 vp)
    {
        Matrix4 local = Matrix4.CreateTranslation(-pivot) * localRot * Matrix4.CreateTranslation(pivot + offset);
        DrawPart(mLegArmModel, local * entityBase * vp);
    }

    public override void TakeDamage(int amount)
    {
        base.TakeDamage(amount);
        var audio = Game.Instance.AudioManager;
        float dist = (Game.Instance.GetPlayer.Position - Position).Length;
        int vol = Proximity(dist, 20f, audio.SfxVol);
        if (vol <= 0) return;

        if (!IsAlive)
        {
            audio.PlayAudio("Resources/Audio/Entities/Skeleton/SkeletonDie.ogg", vol);

            int count = Game.Instance.GameRandom.Next(0, 6);
            if (count > 0)
            {
                var drop = new DroppedItemEntity(Position, ItemStack.FromItem(ItemType.Arrow, count),
                    Game.Instance.WorldTexture);

                Game.Instance.GetWorld.AddEntity(drop);
            }
        }
        else
            audio.PlayAudio("Resources/Audio/Entities/Skeleton/SkeletonHurt.ogg", vol);
    }

    protected override void Fall(World world, float dist)
    {
        int damage = (int)MathF.Ceiling(dist - 3f);
        if (damage > 0) TakeDamage(damage);
    }

    public override void Dispose()
    {
    }
}