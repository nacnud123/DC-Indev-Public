// Spider entity, passive during day, hostile at night. 8-legged, low to the ground. | DA | 3/4/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

public class Spider : Entity
{
    private const string BODY_MODEL = "Resources/Entities/Spider/SpiderBody/SpiderBody.obj";
    private const string BODY_TEXTURE = "Resources/Entities/Spider/SpiderBody/SpiderBody.png";
    private const string HEAD_MODEL = "Resources/Entities/Spider/SpiderHead/SpiderHead.obj";
    private const string HEAD_TEXTURE = "Resources/Entities/Spider/SpiderHead/SpiderHead.png";
    private const string LEG_MODEL = "Resources/Entities/Spider/SpiderLegs/SpiderLegs.obj";
    private const string LEG_TEXTURE = "Resources/Entities/Spider/SpiderLegs/Legs.png";

    private static readonly Vector3 BodyOffset = new(0f, 0.1f, 0f);
    private static readonly Vector3 BodyRotation = new(0f, -90f, 0f);
    private static readonly Vector3 HeadOffset = new(0.15f, 0.1f, 0f);
    private static readonly Vector3 HeadRotation = new(0f, -90f, 0f);
    private static readonly Vector3 LegPivot = new(0f, 0.08f, 0f);

    private static readonly Vector3 Leg1Offset = new(0.07f, 0.065f, -0.115f);
    private static readonly Vector3 Leg1Rotation = new(0f, 70f, 0f);
    private static readonly Vector3 Leg2Offset = new(-0.16f, 0.065f, -0.11f);
    private static readonly Vector3 Leg2Rotation = new(0f, 105f, 0f);
    private static readonly Vector3 Leg3Offset = new(0.075f, 0.065f, 0.115f);
    private static readonly Vector3 Leg3Rotation = new(0f, -75f, 0f);
    private static readonly Vector3 Leg4Offset = new(-0.16f, 0.065f, 0.11f);
    private static readonly Vector3 Leg4Rotation = new(0f, -105f, 0f);

    private const float MAX_LEG_SWING = MathF.PI / 5f;
    private const float WALK_ANIM_SPEED = 8f;
    private const float SWING_DECAY = 0.75f;

    private readonly EntityModel mBodyModel;
    private readonly EntityModel mHeadModel;
    private readonly EntityModel mLegModel;

    private float mWalkPhase;
    private float mLegSwing;

    public override float Width
    {
        get => 1.4f;
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
        get => 3f;
        set { }
    }

    public override int Health { get; set; } = 16;

    public Spider(Vector3 position)
    {
        Position = position;
        InitShader();
        mBodyModel = EntityModel.Load(BODY_MODEL, BODY_TEXTURE);
        mHeadModel = EntityModel.Load(HEAD_MODEL, HEAD_TEXTURE);
        mLegModel = EntityModel.Load(LEG_MODEL, LEG_TEXTURE);
        CurrentAI = new SpiderAi(this);
    }

    public override void Tick(World world)
    {
        base.Tick(world);
        CurrentAI!.Tick(world);
        UpdateAnimation();
    }

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
    }

    protected override void DrawModel(Matrix4 view, Matrix4 projection)
    {
        Matrix4 entityBase = Matrix4.CreateScale(Scale) * Matrix4.CreateRotationY(Yaw) *
                             Matrix4.CreateTranslation(Position);
        Matrix4 vp = view * projection;

        DrawPart(mBodyModel, RotationMatrix(BodyRotation) * Matrix4.CreateTranslation(BodyOffset) * entityBase * vp);
        DrawPart(mHeadModel, RotationMatrix(HeadRotation) * Matrix4.CreateTranslation(HeadOffset) * entityBase * vp);

        float swing1 = MathF.Sin(mWalkPhase) * MAX_LEG_SWING * mLegSwing;
        float swing2 = MathF.Sin(mWalkPhase + MathF.PI) * MAX_LEG_SWING * mLegSwing;

        DrawLeg(swing1, Leg1Offset, Leg1Rotation, entityBase, vp);
        DrawLeg(swing2, Leg2Offset, Leg2Rotation, entityBase, vp);
        DrawLeg(swing1, Leg3Offset, Leg3Rotation, entityBase, vp);
        DrawLeg(swing2, Leg4Offset, Leg4Rotation, entityBase, vp);
    }

    private void DrawLeg(float swingAngle, Vector3 offset, Vector3 rotDeg, Matrix4 entityBase, Matrix4 vp)
    {
        Matrix4 legLocal = Matrix4.CreateTranslation(-LegPivot)
                           * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(rotDeg.X))
                           * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotDeg.Y))
                           * Matrix4.CreateRotationZ(swingAngle + MathHelper.DegreesToRadians(rotDeg.Z))
                           * Matrix4.CreateTranslation(LegPivot + offset);
        DrawPart(mLegModel, legLocal * entityBase * vp);
    }

    private Matrix4 RotationMatrix(Vector3 deg) =>
        Matrix4.CreateRotationX(MathHelper.DegreesToRadians(deg.X))
        * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(deg.Y))
        * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(deg.Z));

    protected override void Fall(World world, float dist)
    {
        int damage = (int)MathF.Ceiling(dist - 3f);

        if (damage > 0)
            TakeDamage(damage);
    }

    public override void Dispose()
    {
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
            audio.PlayAudio("Resources/Audio/Entities/Spider/SpiderDie.ogg", vol);

            int count = Game.Instance.GameRandom.Next(0, 3); // 0-2
            if (count > 0)
            {
                var drop = new DroppedItemEntity(Position, ItemStack.FromItem(ItemType.String, count),
                    Game.Instance.WorldTexture);

                Game.Instance.GetWorld.AddEntity(drop);
            }
        }
        else
        {
            audio.PlayAudio("Resources/Audio/Entities/Spider/SpiderHit.ogg", vol);
        }
    }
}