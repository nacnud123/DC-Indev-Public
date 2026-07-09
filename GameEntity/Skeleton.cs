// Hostile skeleton entity, chases the player and shoots arrows. | DA | 3/2/26


using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

/// <summary>
/// Hostile mob: a skeleton. Chases and shoots arrows at the player via <see cref="SkeletonAi"/> (which owns pathfinding/ranged-attack decisions - this class owns stats, model/animation, and damage/drop handling). Catches fire when standing in direct, unobstructed sunlight, same as Zombie. Unlike Zombie, legs and arms share a single interchangeable "LegArm" model/mesh.
/// </summary>
public class Skeleton : Entity
{
    // Model/texture asset paths. Legs and arms reuse the same mesh (LEGARM_MODEL) since they're visually interchangeable bone limbs.
    private const string BODY_MODEL = "Resources/Entities/Skeleton/SkeletonBody/SkeletonBody.obj";
    private const string BODY_TEXTURE = "Resources/Entities/Skeleton/SkeletonBody/SkeletonBody.png";
    private const string HEAD_MODEL = "Resources/Entities/Skeleton/SkeletonHead/SkeletonHead.obj";
    private const string HEAD_TEXTURE = "Resources/Entities/Skeleton/SkeletonHead/Head.png";
    private const string LEGARM_MODEL = "Resources/Entities/Skeleton/SkeletonLegArm/SkeletonLegArm.obj";
    private const string LEGARM_TEXTURE = "Resources/Entities/Skeleton/SkeletonLegArm/LegArm.png";

    // Animation
    private const float MAX_LIMB_SWING = MathF.PI / 4f;   // radians (45 deg) max leg swing amplitude
    private const float WALK_ANIM_SPEED = 6f;              // how fast walk-cycle phase advances per unit horizontal speed
    private const float SWING_DECAY = 0.75f;               // per-tick multiplier that eases leg swing back to idle when stopped

    // Part offsets: local-space offsets (in model units, pre-Scale) placing each part relative to the entity origin.
    private static readonly Vector3 BodyOffset = new(-.04f, 0.188f, 0f);
    private static readonly Vector3 HeadOffset = new(0f, 0.375f, 0f);
    private static readonly Vector3 LeftLegOff = new(-0.04f, 0f, 0.02f);
    private static readonly Vector3 RightLegOff = new(-0.04f, 0f, -0.075f);
    private static readonly Vector3 LeftArmOff = new(0.202f, 0.187f, -0.126f);
    private static readonly Vector3 RightArmOff = new(0.202f, 0.18f, 0.065f);
    // Pivot points that limb rotations are applied around (hip/shoulder joints, not mesh center).
    private static readonly Vector3 LegPivot = new(0f, 0.1875f, 0f);
    private static readonly Vector3 ArmPivot = new(0f, 0.1875f, 0f);
    // Fixed arm pose angle (-90 deg): arms held down/back at the side, unlike Zombie's forward-raised pose.
    private const float ARM_ANGLE = -MathF.PI / 2f;

    private readonly EntityModel mBodyModel;
    private readonly EntityModel mHeadModel;
    private readonly EntityModel mLegArmModel; // shared mesh used for both legs and both arms

    private float mWalkPhase;   // radians; advances while moving, drives the sinusoidal leg-swing animation
    private float mLimbSwing;   // 0..1 blend factor; 1 while walking, decays toward 0 (SWING_DECAY) when idle

    // Fixed stats: collision box dims, render scale, and movement speed. Setters are no-ops since Skeleton does not allow per-instance variation of these values.
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

    /// <summary>Spawns a skeleton at the given world-space position, loads its part models, and attaches hostile ranged-attack AI.</summary>
    public Skeleton(Vector3 position)
    {
        Position = position;
        InitShader();
        mBodyModel = EntityModel.Load(BODY_MODEL, BODY_TEXTURE);
        mHeadModel = EntityModel.Load(HEAD_MODEL, HEAD_TEXTURE);
        mLegArmModel = EntityModel.Load(LEGARM_MODEL, LEGARM_TEXTURE);
        CurrentAI = new SkeletonAi(this);
    }

    /// <summary>Per-tick update: runs base entity physics, checks/applies sunlight burning, ticks the hostile AI, and advances limb animation.</summary>
    public override void Tick(World world)
    {
        base.Tick(world);
        BurnInSunlight(world);
        CurrentAI!.Tick(world);
        UpdateAnimation();
    }

    /// <summary>
    /// Classic undead behavior: ignite when standing directly under open sky during daylight hours. mTimeOfDay ([0,1), 0=dawn/0.25=noon/0.5=dusk/0.75=midnight, see Game.cs) is mapped to a full sine cycle so sunAngle sweeps 0..2*PI over one day. sunlightLevel = clamp(sin(sunAngle)*2, 0, 1) is >0 for roughly the daylight half of the cycle and saturates to 1 quickly away from the dawn/dusk edges (the *2 steepens the ramp so it isn't a slow fade). Sky light is only MAX_LIGHT when there is a clear, unobstructed path straight up to the sky (a roof or overhang blocks it), so this only burns mobs actually exposed to the sun, not ones merely outside at night or sheltered during the day. See identical logic in Zombie.BurnInSunlight.
    /// </summary>
    private void BurnInSunlight(World world)
    {
        float sunAngle = Game.Instance.TimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0f, 1f);
        if (sunlightLevel <= 0f)
            return; // night time (or dawn/dusk edge) - no burning

        // Sample the sky light directly above the entity's head to check for a clear line to the sky.
        int hx = (int)MathF.Floor(Position.X);
        int hy = (int)MathF.Floor(Position.Y + Height);
        int hz = (int)MathF.Floor(Position.Z);
        // FireTimer counts down in seconds (see Entity); 2f re-arms the burn duration each tick it's still exposed rather than stacking, so the mob stays on fire continuously while in the sun.
        if (world.GetSkyLight(hx, hy, hz) == Chunk.MAX_LIGHT)
            FireTimer = MathF.Max(FireTimer, 2f);
    }

    // Purely cosmetic per-tick limb-swing animation state - does not affect physics or AI decisions.
    private void UpdateAnimation()
    {
        float dt = TickSystem.TICK_DURATION;
        float hSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);

        if (hSpeed > 0.01f)
        {
            // Walk-cycle phase advances proportional to speed so faster movement animates faster (not time-based alone).
            mWalkPhase += hSpeed * WALK_ANIM_SPEED * dt;
            mLimbSwing = 1f;
        }
        else
        {
            // Exponential ease-out back to a neutral stance once the skeleton stops moving.
            mLimbSwing *= SWING_DECAY;
            if (mLimbSwing < 0.01f) mLimbSwing = 0f;
        }
    }

    /// <summary>
    /// Renders the skeleton: body, head, two legs swung in antiphase for the walk cycle, and two arms held in a fixed angled pose (arms never animate with movement, only legs do).
    /// </summary>
    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 entityBase = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationY(Yaw) *
                             Matrix4x4.CreateTranslation(Position);
        Matrix4x4 vp = view * projection;

        DrawPart(mBodyModel, Matrix4x4.CreateTranslation(BodyOffset) * entityBase * vp);
        DrawPart(mHeadModel, Matrix4x4.CreateTranslation(HeadOffset) * entityBase * vp);

        // Legs swing in antiphase (offset by PI) for an alternating walk cycle.
        float swing1 = MathF.Sin(mWalkPhase) * MAX_LIMB_SWING * mLimbSwing;
        float swing2 = MathF.Sin(mWalkPhase + MathF.PI) * MAX_LIMB_SWING * mLimbSwing;

        DrawLimb(Matrix4x4.CreateRotationZ(swing1), LeftLegOff, LegPivot, entityBase, vp);
        DrawLimb(Matrix4x4.CreateRotationZ(swing2), RightLegOff, LegPivot, entityBase, vp);

        // Arms — fixed pose, no walk swing.
        Matrix4x4 armRot = Matrix4x4.CreateRotationZ(ARM_ANGLE);
        DrawLimb(armRot, LeftArmOff, ArmPivot, entityBase, vp);
        DrawLimb(armRot, RightArmOff, ArmPivot, entityBase, vp);
    }

    // Rotates the shared leg/arm mesh about the given pivot by localRot, then offsets it to the target limb position.
    private void DrawLimb(Matrix4x4 localRot, Vector3 offset, Vector3 pivot, Matrix4x4 entityBase, Matrix4x4 vp)
    {
        Matrix4x4 local = Matrix4x4.CreateTranslation(-pivot) * localRot * Matrix4x4.CreateTranslation(pivot + offset);
        DrawPart(mLegArmModel, local * entityBase * vp);
    }

    /// <summary>Applies damage, plays hurt/death sound (volume attenuated by distance), and on death has a chance to drop 0-5 arrows.</summary>
    public override void TakeDamage(int amount)
    {
        base.TakeDamage(amount);
        var audio = Game.Instance.AudioManager;
        float dist = (Game.Instance.GetPlayer.Position - Position).Length();
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