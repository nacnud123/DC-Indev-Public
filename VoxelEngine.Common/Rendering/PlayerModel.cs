// The humanoid used for every player: four meshes, drawn six times, posed from a Pose struct. | Stage 7

using VoxelEngine.GameEntity;
using VoxelEngine.Items;

namespace VoxelEngine.Rendering;

/// <summary>
/// Posing is separate from the entity so the same code draws a remote player and, later, your own
/// in third person. Geometry is the zombie's - same skeleton, and the offsets below are its working
/// values - loaded once and shared by every player, since <c>LoadModel</c> caches by path.
/// </summary>
public static class PlayerModel
{
    private const string BODY_MODEL = "Resources/Entities/Zombie/ZombieBody/ZombieBody.obj";
    private const string BODY_TEXTURE = "Resources/Entities/Zombie/ZombieBody/ZombieBody.png";
    private const string HEAD_MODEL = "Resources/Entities/Zombie/ZombieHead/ZombieHead.obj";
    private const string HEAD_TEXTURE = "Resources/Entities/Zombie/ZombieHead/Head.png";
    private const string LEG_MODEL = "Resources/Entities/Zombie/ZombieLeg/ZombieLeg.obj";
    private const string LEG_TEXTURE = "Resources/Entities/Zombie/ZombieLeg/Leg.png";
    private const string ARM_MODEL = "Resources/Entities/Zombie/ZombieArm/ZombieArm.obj";
    private const string ARM_TEXTURE = "Resources/Entities/Zombie/ZombieArm/Arm.png";

    private static readonly Vector3 BodyOffset = new(-0.06f, 0.188f, 0f);
    private static readonly Vector3 HeadOffset = new(-0.019f, 0.375f, 0f);
    private static readonly Vector3 LeftLegOff = new(-0.06f, 0f, 0f);
    private static readonly Vector3 RightLegOff = new(-0.06f, 0f, -0.061f);
    private static readonly Vector3 LimbPivot = new(0f, 0.1875f, 0f);

    /// The part meshes are authored small; this is Zombie's scale, and they need it to stand 1.8 blocks.
    public const float MODEL_SCALE = 4f;

    private const float MAX_LIMB_SWING = MathF.PI / 4f;
    private const float SNEAK_BODY_PITCH = 0.5f;      // radians the torso leans forward when sneaking
    private const float SNEAK_Y_OFFSET = -0.08f;
    private const float ARM_SWING_ARC = MathF.PI * 0.6f;

    private static readonly Vector3 LeftArmOff = new(-0.06f, 0.165f, -0.125f);
    private static readonly Vector3 RightArmOff = new(-0.06f, 0.165f, 0.06f);

    // Used instead of RightArmOff while holding something - a raised carrying pose wants a
    // different hand position, not just a different angle.
    private static readonly Vector3 RightArmOffHeld = new(-0.06f, 0.145f, 0.06f);

    // Players hang their arms straight down at rest (0), unlike Zombie.cs's forward-stretched pose.
    private const float ARM_ANGLE = 0f;

    // Right arm's rest angle while holding something, raised to a carrying pose.
    private const float HELD_ITEM_ARM_ANGLE = 0.4363f; // 25 degrees

    // Both ItemMesh shapes (cube and thick sprite) span a full 1x1 in X/Y, so one scale fits both.
    private const float HELD_ITEM_SIZE = 0.506f;

    private const float HELD_ITEM_YAW = 0f;

    // Extra nudge for the held item alone, independent of the arm bone it's parented to.
    private static readonly Vector3 HeldItemOffset = new(0.075f, 0.085f, 0.025f);

    private static IRenderHandle? sBody, sHead, sLeg, sArm;

    /// <summary>Everything needed to pose one player this frame.</summary>
    public struct Pose
    {
        public Vector3 Position;
        public float BodyYaw;        // direction the torso faces, radians
        public float HeadYaw;        // where they are LOOKING - can differ from the body, radians
        public float HeadPitch;      // radians
        public float WalkPhase;      // advances with movement
        public float LimbSwing;      // 0..1 blend; 1 walking, decays to 0 when still
        public float ArmSwing;       // 0..1 one-shot, driven by the Animation packet
        public bool IsSneaking;
        public ItemStack? HeldItem;
    }

    // One mesh per distinct stack kind, shared by everyone holding it. Session-lifetime: players
    // swap between a handful of things, and freeing them per swap would churn GPU buffers.
    private static readonly Dictionary<(bool isBlock, int id), IRenderHandle?> sHeldMeshes = new();

    /// <summary>Loads the shared part meshes. Safe to call every frame; only the first does work.</summary>
    public static void EnsureLoaded()
    {
        if (sBody != null)
            return;

        sBody = RenderBackend.Current.LoadModel(BODY_MODEL, BODY_TEXTURE);
        sHead = RenderBackend.Current.LoadModel(HEAD_MODEL, HEAD_TEXTURE);
        sLeg = RenderBackend.Current.LoadModel(LEG_MODEL, LEG_TEXTURE);
        sArm = RenderBackend.Current.LoadModel(ARM_MODEL, ARM_TEXTURE);
    }

    public static void Draw(in Pose pose, Matrix4x4 view, Matrix4x4 projection)
    {
        EnsureLoaded();

        float yOffset = pose.IsSneaking ? SNEAK_Y_OFFSET : 0f;
        var entityBase = Matrix4x4.CreateScale(MODEL_SCALE)
                       * Matrix4x4.CreateRotationY(pose.BodyYaw)
                       * Matrix4x4.CreateTranslation(pose.Position + new Vector3(0f, yOffset, 0f));
        var vp = view * projection;

        // Torso leans forward when sneaking, which is the whole visual tell.
        var bodyRot = pose.IsSneaking ? Matrix4x4.CreateRotationX(SNEAK_BODY_PITCH) : Matrix4x4.Identity;
        Draw(sBody, bodyRot * Matrix4x4.CreateTranslation(BodyOffset) * entityBase * vp);

        // Head tracks the look direction independently of the torso: yaw RELATIVE to the body, plus
        // pitch. Without this players read as mannequins - the head turning is most of what looks alive.
        var headRot = Matrix4x4.CreateRotationX(pose.HeadPitch)
                    * Matrix4x4.CreateRotationY(pose.HeadYaw - pose.BodyYaw);
        Draw(sHead, headRot * Matrix4x4.CreateTranslation(HeadOffset) * entityBase * vp);

        // Legs swing in antiphase.
        float legA = MathF.Sin(pose.WalkPhase) * MAX_LIMB_SWING * pose.LimbSwing;
        float legB = MathF.Sin(pose.WalkPhase + MathF.PI) * MAX_LIMB_SWING * pose.LimbSwing;
        DrawLimb(sLeg, Matrix4x4.CreateRotationZ(legA), LeftLegOff, entityBase, vp);
        DrawLimb(sLeg, Matrix4x4.CreateRotationZ(legB), RightLegOff, entityBase, vp);

        // Arms swing opposite the leg on the same side - left arm forward with right leg - which is
        // what makes a walk cycle read as human rather than as a shambling zombie.
        float armA = ARM_ANGLE + MathF.Sin(pose.WalkPhase + MathF.PI) * MAX_LIMB_SWING * pose.LimbSwing;

        // Holding something lifts the right arm to a carrying pose instead of hanging fully straight.
        float armBRest = pose.HeldItem.HasValue ? HELD_ITEM_ARM_ANGLE : ARM_ANGLE;
        float armB = armBRest + MathF.Sin(pose.WalkPhase) * MAX_LIMB_SWING * pose.LimbSwing;

        // Mining/hitting overrides the walk swing, on the right arm only.
        if (pose.ArmSwing > 0f)
            armB = armBRest - MathF.Sin(pose.ArmSwing * MathF.PI) * ARM_SWING_ARC;

        var rightArmOff = pose.HeldItem.HasValue ? RightArmOffHeld : RightArmOff;

        DrawLimb(sArm, Matrix4x4.CreateRotationZ(armA), LeftArmOff, entityBase, vp);
        DrawLimb(sArm, Matrix4x4.CreateRotationZ(armB), rightArmOff, entityBase, vp);

        if (pose.HeldItem.HasValue)
            DrawHeldItem(pose.HeldItem.Value, armB, rightArmOff, entityBase, vp);
    }

    /// The item in their hand, parented to the right arm so it swings with it.
    private static void DrawHeldItem(ItemStack stack, float armRotation, Vector3 armOffset,
                                     Matrix4x4 entityBase, Matrix4x4 vp)
    {
        var mesh = HeldMesh(stack);
        if (mesh == null)
            return;

        // Position only, not rotation - rotating the item with the arm tips it flat as the arm raises.
        var handPos = Vector3.Transform(-LimbPivot, Matrix4x4.CreateRotationZ(armRotation))
                    + LimbPivot + armOffset + HeldItemOffset;

        bool isCube = ItemMesh.IsCube(stack);

        // ItemMesh's shapes span 0..1 in X/Y with their origin at a corner, not their middle. The
        // cube also spans 0..1 in Z; the thick sprite is already centred there.
        var centre = Matrix4x4.CreateTranslation(-0.5f, -0.5f, isCube ? -0.5f : 0f);
        var orient = Matrix4x4.CreateRotationY(HELD_ITEM_YAW);

        var transform = centre
                      * Matrix4x4.CreateScale(HELD_ITEM_SIZE / MODEL_SCALE)
                      * orient
                      * Matrix4x4.CreateTranslation(handPos) * entityBase * vp;

        var backend = RenderBackend.Current;
        var atlas = isCube || stack.IsBlock ? backend.WorldAtlas : backend.ItemAtlas;

        // The thick sprite's side faces aren't reliably outward-wound, so it stays double-sided.
        backend.DrawMesh(mesh, atlas, transform, doubleSided: !isCube);
    }

    private static IRenderHandle? HeldMesh(ItemStack stack)
    {
        var key = (stack.IsBlock, stack.IsBlock ? (int)stack.Block : (int)stack.Item);

        if (sHeldMeshes.TryGetValue(key, out var cached))
            return cached;

        var vertices = ItemMesh.Build(stack);
        var mesh = RenderBackend.Current.CreateMesh(vertices, vertices.Length / ItemMesh.VERTEX_STRIDE);

        sHeldMeshes[key] = mesh;
        return mesh;
    }

    private static void DrawLimb(IRenderHandle? model, Matrix4x4 localRot, Vector3 offset,
                                 Matrix4x4 entityBase, Matrix4x4 vp)
    {
        var local = Matrix4x4.CreateTranslation(-LimbPivot) * localRot
                  * Matrix4x4.CreateTranslation(LimbPivot + offset);
        Draw(model, local * entityBase * vp);
    }

    private static void Draw(IRenderHandle? model, Matrix4x4 mvp)
    {
        if (model != null)
            RenderBackend.Current.DrawModel(model, mvp);
    }
}
