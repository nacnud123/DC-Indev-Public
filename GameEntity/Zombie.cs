// Hostile zombie entity, chases the player and attacks with melee. | DA | 3/2/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

public class Zombie : Entity
{
    private const string BODY_MODEL = "Resources/Entities/Zombie/ZombieBody/ZombieBody.obj";
    private const string BODY_TEXTURE = "Resources/Entities/Zombie/ZombieBody/ZombieBody.png";
    private const string HEAD_MODEL = "Resources/Entities/Zombie/ZombieHead/ZombieHead.obj";
    private const string HEAD_TEXTURE = "Resources/Entities/Zombie/ZombieHead/Head.png";
    private const string LEG_MODEL = "Resources/Entities/Zombie/ZombieLeg/ZombieLeg.obj";
    private const string LEG_TEXTURE = "Resources/Entities/Zombie/ZombieLeg/Leg.png";
    private const string ARM_MODEL = "Resources/Entities/Zombie/ZombieArm/ZombieArm.obj";
    private const string ARM_TEXTURE = "Resources/Entities/Zombie/ZombieArm/Arm.png";

    // Animation
    private const float MAX_LIMB_SWING = MathF.PI / 4f;
    private const float WALK_ANIM_SPEED = 6f;
    private const float SWING_DECAY = 0.75f;

    // Part offsets
    private static readonly Vector3 BodyOffset = new(-0.06f, 0.188f, 0f);
    private static readonly Vector3 HeadOffset = new(-0.019f, 0.375f, 0f);
    private static readonly Vector3 LeftLegOff = new(-0.06f, 0f, 0f);
    private static readonly Vector3 RightLegOff = new(-0.06f, 0f, -0.061f);
    private static readonly Vector3 LeftArmOff = new(-0.03f, 0.12f, -0.1f);
    private static readonly Vector3 RightArmOff = new(-0.03f, 0.12f, 0.03f);
    private static readonly Vector3 LegPivot = new(0f, 0.1875f, 0f);
    private static readonly Vector3 ArmPivot = new(0f, 0.1875f, 0f);

    // Arms raised forward 90° — classic zombie pose
    private const float ARM_ANGLE = MathF.PI * 0.5f;

    private readonly EntityModel mBodyModel;
    private readonly EntityModel mHeadModel;
    private readonly EntityModel mLegModel;
    private readonly EntityModel mArmModel;

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
        get => 2.5f;
        set { }
    }

    public override int Health { get; set; } = 20;

    public Zombie(Vector3 position)
    {
        Position = position;
        InitShader();
        mBodyModel = EntityModel.Load(BODY_MODEL, BODY_TEXTURE);
        mHeadModel = EntityModel.Load(HEAD_MODEL, HEAD_TEXTURE);
        mLegModel = EntityModel.Load(LEG_MODEL, LEG_TEXTURE);
        mArmModel = EntityModel.Load(ARM_MODEL, ARM_TEXTURE);
        CurrentAI = new ZombieAi(this);
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

        DrawLegLimb(Matrix4.CreateRotationZ(swing1), LeftLegOff, LegPivot, entityBase, vp);
        DrawLegLimb(Matrix4.CreateRotationZ(swing2), RightLegOff, LegPivot, entityBase, vp);

        // Arms — fixed outstretched pose, no walk swing
        Matrix4 armRot = Matrix4.CreateRotationZ(ARM_ANGLE);
        DrawArmLimb(armRot, LeftArmOff, ArmPivot, entityBase, vp);
        DrawArmLimb(armRot, RightArmOff, ArmPivot, entityBase, vp);
    }

    private void DrawLegLimb(Matrix4 localRot, Vector3 offset, Vector3 pivot, Matrix4 entityBase, Matrix4 vp)
    {
        Matrix4 local = Matrix4.CreateTranslation(-pivot) * localRot * Matrix4.CreateTranslation(pivot + offset);
        DrawPart(mLegModel, local * entityBase * vp);
    }

    private void DrawArmLimb(Matrix4 localRot, Vector3 offset, Vector3 pivot, Matrix4 entityBase, Matrix4 vp)
    {
        Matrix4 local = Matrix4.CreateTranslation(-pivot) * localRot * Matrix4.CreateTranslation(pivot + offset);
        DrawPart(mArmModel, local * entityBase * vp);
    }

    public override void TakeDamage(int amount)
    {
        base.TakeDamage(amount);
        var audio = Game.Instance.AudioManager;
        float dist = (Game.Instance.GetPlayer.Position - Position).Length;
        int vol = Proximity(dist, 20f, audio.SfxVol);

        if (vol <= 0)
            return;

        if (!IsAlive)
        {
            audio.PlayAudio("Resources/Audio/Entities/Zombie/ZombieDie.ogg", vol);

            int count = Game.Instance.GameRandom.Next(0, 2);
            if (count > 0)
            {
                var drop = new DroppedItemEntity(Position, ItemStack.FromItem(ItemType.Feather, count),
                    Game.Instance.WorldTexture);

                Game.Instance.GetWorld.AddEntity(drop);
            }
        }
        else
            audio.PlayAudio("Resources/Audio/Entities/Zombie/ZombieHit.ogg", vol);
    }

    protected override void Fall(World world, float dist)
    {
        int damage = (int)MathF.Ceiling(dist - 3f);

        if (damage > 0)
            TakeDamage(damage);
    }

    public override void Dispose()
    {
    }
}