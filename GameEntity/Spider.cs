// Spider entity, passive during day, hostile at night. 8-legged, low to the ground. | DA | 3/4/26


using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

/// <summary>
/// Hostile-at-night, passive-by-day mob (see SpiderAi for the day/night behavior switch). Rendered as three separately-animated OBJ parts (body/head/4 legs reused via 4 offsets) rather than a single skinned mesh, so DrawModel manually composes a transform per part each frame.
/// </summary>
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

    // All offset/rotation constants above were hand-tuned to align the shared leg model against the body mesh at each of the 4 attachment points - not derived from any formula.
    private const float MAX_LEG_SWING = MathF.PI / 5f;   // Peak swing angle (radians) at full walk speed.
    private const float WALK_ANIM_SPEED = 8f;            // How fast the walk-cycle phase advances per unit of horizontal speed.
    private const float SWING_DECAY = 0.75f;             // Per-tick multiplier that eases leg swing back to rest once the spider stops.

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

    // Advances the walk-cycle phase based on horizontal speed (so faster movement = faster leg animation, independent of tick rate) and eases the swing amplitude back to 0 when stationary.
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

    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        // Shared root transform (scale -> yaw -> world position) that every part is drawn relative to.
        Matrix4x4 entityBase = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationY(Yaw) *
                             Matrix4x4.CreateTranslation(Position);
        Matrix4x4 vp = view * projection;

        DrawPart(mBodyModel, RotationMatrix(BodyRotation) * Matrix4x4.CreateTranslation(BodyOffset) * entityBase * vp);
        DrawPart(mHeadModel, RotationMatrix(HeadRotation) * Matrix4x4.CreateTranslation(HeadOffset) * entityBase * vp);

        // Legs 1&3 and 2&4 swing in opposite phase (offset by PI) to give an alternating spider gait using only two swing values instead of computing a phase per leg.
        float swing1 = MathF.Sin(mWalkPhase) * MAX_LEG_SWING * mLegSwing;
        float swing2 = MathF.Sin(mWalkPhase + MathF.PI) * MAX_LEG_SWING * mLegSwing;

        DrawLeg(swing1, Leg1Offset, Leg1Rotation, entityBase, vp);
        DrawLeg(swing2, Leg2Offset, Leg2Rotation, entityBase, vp);
        DrawLeg(swing1, Leg3Offset, Leg3Rotation, entityBase, vp);
        DrawLeg(swing2, Leg4Offset, Leg4Rotation, entityBase, vp);
    }

    // Translate to the pivot, apply the leg's fixed rotation (plus animated swing on Z), then translate out to its attachment offset - so the swing rotates the leg about its hip, not the origin.
    private void DrawLeg(float swingAngle, Vector3 offset, Vector3 rotDeg, Matrix4x4 entityBase, Matrix4x4 vp)
    {
        Matrix4x4 legLocal = Matrix4x4.CreateTranslation(-LegPivot)
                           * Matrix4x4.CreateRotationX(float.DegreesToRadians(rotDeg.X))
                           * Matrix4x4.CreateRotationY(float.DegreesToRadians(rotDeg.Y))
                           * Matrix4x4.CreateRotationZ(swingAngle + float.DegreesToRadians(rotDeg.Z))
                           * Matrix4x4.CreateTranslation(LegPivot + offset);
        DrawPart(mLegModel, legLocal * entityBase * vp);
    }

    private Matrix4x4 RotationMatrix(Vector3 deg) =>
        Matrix4x4.CreateRotationX(float.DegreesToRadians(deg.X))
        * Matrix4x4.CreateRotationY(float.DegreesToRadians(deg.Y))
        * Matrix4x4.CreateRotationZ(float.DegreesToRadians(deg.Z));

    // Spiders take no damage from falls under 3 blocks (they're natural climbers/jumpers).
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
        float dist = (Game.Instance.GetPlayer.Position - Position).Length();
        // Distance-attenuated volume (falls off over 20 blocks) so hurt sounds don't play at full volume for spiders far from the player.
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