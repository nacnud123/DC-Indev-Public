// A block lying on the ground. Bounces, bobs, spins, and is picked up by the player. | DA | 3/2/26

using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

/// <summary>
/// A physical item/block stack sitting in the world after being dropped (block break, death, etc.).
/// Owns its own tiny GL mesh instead of going through ChunkMeshBuilder/EntityModel: block drops get
/// a small cube built from the block's own face textures, everything else gets a 2-triangle
/// billboard quad textured from the item atlas. Ticks its own arcade-style physics (gravity, drag,
/// bounce, ground friction) and despawns either after MAX_AGE ticks or when picked up by the player.
/// </summary>
public class DroppedItemEntity : Entity
{
    public override bool IsTargetable => false;
    public override float ShadowSize => 0.2f;

    private const float ITEM_GRAVITY = 8f;
    private const float TERMINAL_VEL = 20f;
    private const float DRAG = 0.98f;           // Per-tick velocity multiplier (air resistance), applied every tick regardless of ground contact.
    private const float GROUND_FRICTION = 0.7f; // Additional per-tick XZ multiplier applied only while resting on the ground.
    private const float BOUNCE = 0.4f;          // Fraction of downward velocity restored as upward velocity on landing.
    private const float PICKUP_RADIUS = 2f;     // Blocks; player must be within this distance for auto-pickup.
    private const int MAX_AGE = 6000;           // Ticks before the item despawns unpicked (~5 minutes at 20 ticks/sec).
    private const int PICKUP_DELAY = 10;        // Ticks after spawning before it can be picked up (prevents instant re-pickup of just-broken blocks).
    private const float SPIN_SPEED = 1.2f;      // Radians/sec the cube-rendered drop rotates for visual flair.

    // Vertex layout: 3 floats position + 2 floats UV + 3 floats normal = 8 floats per vertex.
    private const int VERTEX_STRIDE = 8;

    private ItemStack mStack;

    /// Not readonly: a pickup into an almost-full inventory takes what fits and leaves the rest
    /// lying there, which means shrinking the stack this entity carries.
    public ItemStack Stack
    {
        get => mStack;
        internal set => mStack = value;
    }
    private readonly bool mIsCubeBlock;
    private readonly IRenderHandle? mRenderAtlas;
    private IRenderHandle? mMesh;
    private int mVertexCount;

    private float mSpinAngle;
    private readonly float mBobPhase;
    private int mAge;
    private int mPickupDelay = PICKUP_DELAY;

    // The world atlas used to be a required constructor parameter, which meant shared code
    // couldn't drop an item without a GL texture in hand - a headless host could never call
    // Block.Explode. The atlas now comes from the render backend, which supplies a null handle
    // when there's no GPU.
    public DroppedItemEntity(Vector3 position, ItemStack stack)
    {
        Position = position;
        mStack = stack;
        Width = 0.25f;
        Height = 0.25f;

        var rng = GameContext.Current.GameRandom;
        Velocity = new Vector3(
            (float)(rng.NextDouble() - 0.5) * 1.0f,
            3.0f,
            (float)(rng.NextDouble() - 0.5) * 1.0f
        );

        mBobPhase = (float)(rng.NextDouble() * MathF.PI * 2f);

        // Only cube-like block render types get a 3D cube mesh; other block types (e.g. torches,
        // crops - anything cross-shaped or non-cuboid) fall back to the flat billboard like items.
        if (stack.IsBlock)
        {
            mIsCubeBlock = ItemMesh.IsCube(stack);
        }

        var backend = RenderBackend.Current;
        mRenderAtlas = (mIsCubeBlock || stack.IsBlock) ? backend.WorldAtlas : backend.ItemAtlas;

        UploadMesh(ItemMesh.Build(stack));
    }

    // The vertex array is built on the CPU either way - it's cheap, and keeping it unconditional
    // keeps this code identical on client and server. Only the upload is skipped when there's no
    // GPU, where CreateMesh returns a null handle.
    private void UploadMesh(float[] arr)
    {
        mVertexCount = arr.Length / ItemMesh.VERTEX_STRIDE;
        mMesh = RenderBackend.Current.CreateMesh(arr, mVertexCount);
    }

    // Server-owned drops don't tick; the bob is cosmetic and still has to run.
    protected override void TickProxyAnimation() => mAge++;

    public override void Tick(World world)
    {
        float dt = TickSystem.TICK_DURATION;

        if (mPickupDelay > 0)
            mPickupDelay--;

        if (++mAge >= MAX_AGE)
        {
            IsAlive = false;
            return;
        }

        mSpinAngle += SPIN_SPEED * dt;

        // In a fluid the drop floats to the surface and rides the current instead of falling; this
        // entity runs its own arcade physics rather than Entity.Tick's, so it opts in explicitly.
        var velocity = Velocity;
        bool inFluid = ApplyFluidForces(world, Position, ref velocity);

        if (!inFluid)
        {
            velocity.Y -= ITEM_GRAVITY * dt;
            if (velocity.Y < -TERMINAL_VEL)
                velocity.Y = -TERMINAL_VEL;
        }

        Velocity = velocity;

        // Sample the block at the entity's vertical center; if it's lava, give the item a random
        // upward "pop" kick so it doesn't just sit and burn silently in place.
        int bx = (int)MathF.Floor(Position.X);
        int by = (int)MathF.Floor(Position.Y + Height * 0.5f);
        int bz = (int)MathF.Floor(Position.Z);
        if (world.GetBlock(bx, by, bz) == BlockType.Lava)
        {
            var rng = GameContext.Current.GameRandom;
            Velocity = new Vector3(
                (float)(rng.NextDouble() - 0.5) * 4f,
                4.0f,
                (float)(rng.NextDouble() - 0.5) * 4f
            );
        }

        Vector3 frameVel = Velocity * dt;
        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVel);
        Position += actual;

        // If collision resolution shortened the Y movement noticeably vs. the requested Y velocity,
        // something was hit vertically (floor or ceiling) - used instead of a direct IsOnGround
        // check because it also detects hitting a ceiling while moving upward.
        bool hitY = MathF.Abs(actual.Y) < MathF.Abs(frameVel.Y) * 0.99f;
        if (hitY)
        {
            if (Velocity.Y < 0)
            {
                IsOnGround = true;
                // Small bounces (< 0.5 units/sec resulting velocity) are clamped to a full stop
                // instead of bouncing forever with diminishing amplitude.
                float bounce = -Velocity.Y * BOUNCE;
                Velocity = new Vector3(Velocity.X, bounce < 0.5f ? 0f : bounce, Velocity.Z);
            }
            else
            {
                Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
            }
        }
        else
        {
            IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
        }

        // Air resistance and ground friction only apply out of a fluid - the fluid's own, much
        // heavier drag was already applied above, and stacking the two would stall the drop.
        if (!inFluid)
        {
            Velocity *= DRAG;

            if (IsOnGround)
                Velocity = new Vector3(Velocity.X * GROUND_FRICTION, Velocity.Y, Velocity.Z * GROUND_FRICTION);
        }

        if (mPickupDelay == 0)
        {
            // Singleplayer only. A server has many players and no "the" player - GetPlayer is null
            // there by design - and owns pickup itself, so this whole branch is skipped.
            var player = GameContext.Current.GetPlayer;
            var inv = GameContext.Current.PlayerInventory;

            if (player != null && inv != null && (player.Position - Position).Length() < PICKUP_RADIUS)
            {
                // Same partial-add trap the server pickup had: TryAdd returns true when only part
                // of the stack fit, so killing the entity on it deleted the rest. Here the entity
                // is local, so the remainder just stays on it.
                var leftover = inv.AddGetRemainder(mStack);

                if (leftover is not { } rest || rest.Count < mStack.Count)
                    GameContext.Current.AudioManager.PlayPickupSound();

                if (leftover is { } remainder)
                    mStack = remainder;
                else
                    IsAlive = false;
            }
        }
    }

    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        // Oscillates between 0 and 0.2 units of vertical offset; mBobPhase randomizes each drop's
        // starting phase so a pile of items doesn't bob in visible unison.
        float bob = MathF.Sin(mAge / 10.0f + mBobPhase) * 0.1f + 0.1f;

        if (mIsCubeBlock)
            DrawCube(view, projection, bob);
        else
            DrawBillboard(view, projection, bob);
    }

    private void DrawCube(Matrix4x4 view, Matrix4x4 projection, float bob)
    {
        Matrix4x4 mvp =
            Matrix4x4.CreateTranslation(-0.5f, 0f, -0.5f)
            * Matrix4x4.CreateScale(0.25f)
            * Matrix4x4.CreateRotationY(mSpinAngle)
            * Matrix4x4.CreateTranslation(Position + new Vector3(0f, bob, 0f))
            * view
            * projection;

        if (mMesh != null)
            RenderBackend.Current.DrawMesh(mMesh, mRenderAtlas, mvp);
    }

    private void DrawBillboard(Matrix4x4 view, Matrix4x4 projection, float bob)
    {
        // Y-axis-only billboarding: rotate the quad to face the camera's XZ direction but never
        // tilt it up/down, so it always reads as a flat sprite standing upright on the ground.
        // Render-only, so the local player always exists here - a server never draws.
        var viewer = GameContext.Current.GetPlayer;
        float dx = viewer.Camera.Position.X - Position.X;
        float dz = viewer.Camera.Position.Z - Position.Z;

        Matrix4x4 mvp =
            Matrix4x4.CreateRotationY(MathF.Atan2(dx, dz))
            * Matrix4x4.CreateTranslation(Position + new Vector3(0f, bob, 0f))
            * view
            * projection;

        if (mMesh != null)
            RenderBackend.Current.DrawMesh(mMesh, mRenderAtlas, mvp);
    }

    public override void Dispose()
    {
        mMesh?.Dispose();
        mMesh = null;
    }
}
