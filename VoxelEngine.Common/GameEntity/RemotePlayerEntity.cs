// Another player, as seen by this client. Position comes only from packets. | Stage 7

using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

/// <summary>
/// Holds the interpolation and animation state for one remote player; <see cref="PlayerModel"/> does
/// the drawing. Never runs physics or AI - the server owns where other players are, and simulating
/// them here would fight its updates and jitter.
/// </summary>
public sealed class RemotePlayerEntity : Entity
{
    private const float WALK_ANIM_SPEED = 6f;
    private const float SWING_DECAY = 0.75f;
    private const float SWING_RAMP = 0.3f;

    // Fraction of the gap to the latest server position closed per tick. Matches mobs' PROXY_LERP
    // (Entity.cs) - lower values round off jumps into a floaty arc instead of a snappy one, since the
    // server already sends a position most ticks while moving.
    private const float LERP_RATE = 0.6f;

    // One-shot arm swing over ~6 ticks (0.3s at 20 TPS).
    private const float ARM_SWING_STEP = 1f / 6f;

    private Vector3 mPrevServerPos, mLatestServerPos;
    private float mPrevYaw, mLatestYaw, mLatestPitch;
    private float mAlpha;

    private float mWalkPhase, mLimbSwing, mArmSwing;

    public string Name = "";
    public bool IsSneaking;

    /// From EntityEquipment. Null when their hand is empty.
    public ItemStack? HeldItem;

    // Targetable, but never damaged locally: an attack on one of these becomes a UseEntity packet
    // and the server decides what it did (Stage 11).
    public override bool IsTargetable => true;
    public override float Width { get; set; } = 0.6f;
    public override float Height { get; set; } = 1.8f;
    public override float Scale { get => PlayerModel.MODEL_SCALE; set { } }

    public RemotePlayerEntity(int entityId, string name, Vector3 position, float yawDegrees, float pitchDegrees)
    {
        AssignNetworkId(entityId);
        Name = name;

        Position = position;
        Yaw = ToMeshYaw(yawDegrees);

        mPrevServerPos = mLatestServerPos = position;
        mPrevYaw = mLatestYaw = Yaw;
        mLatestPitch = float.DegreesToRadians(pitchDegrees);
        mAlpha = 1f;

        InitShader();
        PlayerModel.EnsureLoaded();
    }

    /// The latest position the server sent, before interpolation. Relative moves apply to this, not
    /// to the smoothed Position, or rounding error compounds between absolute resyncs.
    public Vector3 ServerPosition => mLatestServerPos;

    /// Mesh-convention radians (see ToMeshYaw), not the wire's raw degrees.
    public float ServerYaw => mLatestYaw;
    public float ServerPitch => mLatestPitch;

    /// From EntityTeleport (absolute) and EntityRelativeMove / EntityLookRelMove (deltas). Yaw/pitch
    /// arrive in degrees off the wire, converted to radians here.
    public void OnServerPosition(Vector3 position, float yawDegrees, float pitchDegrees)
    {
        mPrevServerPos = Position;
        mPrevYaw = Yaw;
        mLatestServerPos = position;
        mLatestYaw = ToMeshYaw(yawDegrees);
        mLatestPitch = float.DegreesToRadians(pitchDegrees);
        mAlpha = 0f;
    }

    /// From EntityLook - rotation only, position unchanged.
    public void OnServerLook(float yawDegrees, float pitchDegrees)
    {
        mPrevYaw = Yaw;
        mLatestYaw = ToMeshYaw(yawDegrees);
        mLatestPitch = float.DegreesToRadians(pitchDegrees);
        mAlpha = 0f;
    }

    // Camera.Yaw and this mesh rig's CreateRotationY use opposite conventions (see EntityAi's
    // atan2(dx,dz) - PI/2 for the same rig) - a straight negation converts between them.
    private static float ToMeshYaw(float cameraYawDegrees) => -float.DegreesToRadians(cameraYawDegrees);

    /// From Animation (0x12) - the arm swing when they mine or hit something.
    public void OnSwingArm() => mArmSwing = ARM_SWING_STEP;

    // No base.Tick: that runs gravity and collision, which the server already did.
    public override void Tick(World world)
    {
        mAlpha = MathF.Min(1f, mAlpha + LERP_RATE);

        var previous = Position;
        Position = Vector3.Lerp(mPrevServerPos, mLatestServerPos, mAlpha);
        Yaw = LerpAngle(mPrevYaw, mLatestYaw, mAlpha);

        // The walk cycle is inferred from actual movement - the server never says "this player is
        // walking", the same way Zombie infers its own from velocity.
        float horizontalSpeed = new Vector2(Position.X - previous.X, Position.Z - previous.Z).Length();
        if (horizontalSpeed > 0.001f)
        {
            mWalkPhase += horizontalSpeed * WALK_ANIM_SPEED;
            mLimbSwing = MathF.Min(1f, mLimbSwing + SWING_RAMP);
        }
        else
        {
            mLimbSwing *= SWING_DECAY;
            if (mLimbSwing < 0.01f) mLimbSwing = 0f;
        }

        if (mArmSwing > 0f)
        {
            mArmSwing += ARM_SWING_STEP;
            if (mArmSwing >= 1f) mArmSwing = 0f;
        }
    }

    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        PlayerModel.Draw(new PlayerModel.Pose
        {
            Position = Position,
            BodyYaw = Yaw,
            HeadYaw = mLatestYaw,
            HeadPitch = mLatestPitch,
            WalkPhase = mWalkPhase,
            LimbSwing = mLimbSwing,
            ArmSwing = mArmSwing,
            IsSneaking = IsSneaking,
            HeldItem = HeldItem,
        }, view, projection);
    }

    // Shortest-path angle lerp (radians) - without this, a player turning past 180 degrees spins the
    // long way.
    private static float LerpAngle(float a, float b, float t)
    {
        float delta = ((b - a + 3f * MathF.PI) % (2f * MathF.PI)) - MathF.PI;
        return a + delta * t;
    }
}
