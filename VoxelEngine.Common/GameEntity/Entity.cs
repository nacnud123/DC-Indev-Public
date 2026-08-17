// Base entity class - position, velocity, physics, lighting, hit flash, fire, and model rendering | DA | 2/5/26 - 2/14/26
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

// Base class for anything that moves around the world and isn't a block: the player, mobs
// (Pig, Zombie, Stalker...), dropped items, TNT, etc. Handles the stuff they all share -
// position/velocity, gravity and collision, taking fall/fire damage, and drawing a textured
// model. Subclasses override Tick/DrawModel for their own behavior and looks.
public class Entity
{
    // The shared entity shader used to live here as a Shader field. It's now owned by the render
    // backend, because Shader wraps a GL program and can't cross into Common - see
    // Rendering/IRenderBackend.cs. Uniform writes below go through RenderBackend.Current.

    // Frame-constant lighting state - set each frame by GameRenderer before rendering
    public static Vector3 LightDir = new(-0.5f, -1f, -0.3f); // directional "sun" light direction, shared by all entities this frame
    public static float AmbientStrength = 0.4f;
    public static float SunlightLevel = 1f; // scales directional light by time-of-day/weather
    internal static Vector3 CameraPosition; // eye position for fire billboard yaw

    // Tick-constant audio/game state - set by Game before ticking entities
    internal static Vector3 ListenerPosition; // player position for step-sound proximity
    internal static int SfxVol;
    internal static Action<Terrain.BlockBreakMaterial, int>? PlayStepSoundCallback;
    internal static Random? SharedRandom;

    public virtual bool IsTargetable => true; // whether raycasts/attacks can hit this entity (arrows and paintings-in-flight override to false/true as appropriate)
    public virtual float ShadowSize => 0.5f; // radius of the blob shadow drawn under the entity; 0 disables it
    public virtual int Health { get; set; } = 100;
    public virtual float Width { get; set; } = 0.6f; // horizontal AABB size in blocks (both X and Z)
    public virtual float Height { get; set; } = 1.8f; // vertical AABB size in blocks
    public virtual float EyeHeight { get; set; } = 1.62f; // camera/eye offset above Position.Y, used by player-like entities
    public virtual float WalkSpeed { get; set; } = 4.317f; // blocks/second, matches vanilla Minecraft's walk speed
    public virtual float SlowWalkSpeed { get; set; } = 2.1585f; // blocks/second, e.g. sneaking (exactly half WalkSpeed)
    public virtual float Scale { get; set; } = 1f;
    public virtual float JumpForce { get; set; }
    public float Yaw   { get; set; }
    public float Pitch { get; set; }
    public bool IsOnGround { get; protected set; }
    public bool IsAlive { get; set; } = true;
    private float hitFlashTimer = 0; // seconds remaining of the white "just hit" flash on the model shader
    const float HIT_FLASH_DURATION = 0.3f;
    private float mStepTimer; // counts down between footstep sounds while walking on the ground
    private const float STEP_INTERVAL = 0.5f; // seconds between footstep sounds

    public float FireTimer { get; set; } // seconds remaining that this entity is on fire
    public bool IsOnFire => FireTimer > 0f;
    private float mFireDamageTimer; // seconds until the next tick of fire damage is applied (ticks once per second while on fire)
    protected float mFallDistance; // accumulated fall distance in blocks since last touching ground, used to compute fall damage on landing

    // Opaque handle to this entity's model, if it has a single one. Mobs with multiple parts
    // (head/body/legs) hold their own handles instead and override DrawModel.
    protected IRenderHandle? Model { get; set; }

    // Backing fields for Position/Velocity - kept private so subclasses go through the properties
    // (mirrors the get/set pattern used for Health/Width/etc. above, though here it's a plain
    // non-virtual property since position/velocity aren't meant to be overridden per-subclass).
    private Vector3 mPos;
    private Vector3 mVel;

    // World-space position in blocks. For most entities this is the *feet* position - the AABB
    // (see GetBoundingBox) extends upward from here by Height, not centered on it.
    public Vector3 Position
    {
        get => mPos;
        set => mPos = value;
    }

    // Current velocity in blocks/second (integrated each tick in Tick() using TICK_DURATION).
    public Vector3 Velocity
    {
        get => mVel;
        set => mVel = value;
    }

    public EntityAi? CurrentAI;

    // ---- network identity ------------------------------------------------------------------
    //
    // No entity id existed before; every entity packet needs one. Interlocked because entities
    // are constructed from worker threads during chunk loading as well as from the main thread.
    //
    // NOTE: client-generated and server-generated ids both start at 1 and will collide once a
    // client connects to a server. The fix (a client-side id offset, or ids assigned exclusively
    // by the server via AssignNetworkId) belongs with the protocol work, not here.
    private static int sNextId;
    public int Id { get; private set; } = Interlocked.Increment(ref sNextId);

    /// <summary>Overrides the locally generated id with one assigned by the server.</summary>
    internal void AssignNetworkId(int id)
    {
        Id = id;
        HasNetworkId = true;
    }

    /// <summary>
    /// False while an entity still carries the id its constructor gave it. Anything the server
    /// creates internally - a mob from MobSpawner, an arrow, a TNT block - starts out like this,
    /// and those ids come from a different counter than <see cref="AllocateId"/>: leave one
    /// unassigned and it will eventually share an id with a dropped item, at which point movement
    /// packets go to the wrong entity and destroying one destroys the other.
    /// </summary>
    public bool HasNetworkId { get; private set; }

    /// <summary>
    /// Someone else owns where this entity is: a mob on a multiplayer client, or a player on the
    /// server. World.TickEntities runs <see cref="TickProxy"/> instead of the normal tick - running
    /// physics or AI here would fight the positions arriving over the wire.
    /// </summary>
    public bool IsRemoteProxy;

    /// The last position its owner sent. TickProxy eases toward it rather than snapping, because
    /// updates arrive every few ticks and a snap reads as a stutter.
    public Vector3 NetTargetPosition;

    // Updates arrive every tick while something is moving, so this can be high: at 0.35 a thrown
    // item trailed its real position by ~2 ticks and looked like it was falling in slow motion.
    private const float PROXY_LERP = 0.6f;

    public void TickProxy(World world)
    {
        var delta = NetTargetPosition - mPos;

        // Animation code reads Velocity to decide whether the legs move, so give it the real one.
        mVel = delta / TickSystem.TICK_DURATION;
        mPos += delta * PROXY_LERP;

        TickProxyAnimation();
    }

    /// Mobs override this to keep their walk cycle running; they animate off Velocity, which
    /// TickProxy fills in.
    protected virtual void TickProxyAnimation() { }

    // Movement-delta bookkeeping for the entity-update packets that replace full position sends.
    internal Vector3 LastSentPosition;
    internal int TicksSinceUpdate;

    // Kept as a no-op so the many subclass constructors that call it don't all need editing.
    // Shader creation is now the backend's business and happens when the client binds it; a
    // headless host never compiles a shader at all.
    internal static void InitShader() { }

    // --- Fluid forces ---------------------------------------------------------------------------
    //
    // Per-tick constants, converted to the blocks/second Velocity everything else here uses by
    // dividing by TICK_DURATION. Buoyancy deliberately exceeds fluid gravity, so a submerged entity
    // rises until its feet break the surface and then bobs there - which is how a dropped item ends
    // up floating downstream instead of sitting on the riverbed.
    private const float FLUID_GRAVITY_PER_TICK = 0.02f;
    private const float FLUID_BUOYANCY_PER_TICK = 0.03f;
    private const float WATER_DRAG_PER_TICK = 0.8f;
    private const float LAVA_DRAG_PER_TICK = 0.5f;

    /// <summary>The fluid this entity is standing in, or Air. Respects the fluid's level.</summary>
    protected static BlockType FluidAt(World world, Vector3 position)
    {
        if (BlockFluid.ContainsPoint(world, BlockType.Water, position))
            return BlockType.Water;

        if (BlockFluid.ContainsPoint(world, BlockType.Lava, position))
            return BlockType.Lava;

        return BlockType.Air;
    }

    /// <summary>
    /// Replaces air gravity with buoyancy, drag and the current for an entity in a fluid. Returns
    /// false (leaving <paramref name="velocity"/> untouched) when the entity isn't in one, so
    /// callers can fall through to their normal gravity path.
    /// </summary>
    protected static bool ApplyFluidForces(World world, Vector3 position, ref Vector3 velocity)
    {
        var fluid = FluidAt(world, position);
        if (fluid == BlockType.Air)
            return false;

        float dt = TickSystem.TICK_DURATION;
        float drag = fluid == BlockType.Water ? WATER_DRAG_PER_TICK : LAVA_DRAG_PER_TICK;

        velocity.Y -= FLUID_GRAVITY_PER_TICK / dt;
        velocity.Y += FLUID_BUOYANCY_PER_TICK / dt;

        // Carried by the current, same push the player gets.
        var flow = BlockFluid.FlowDirection(world,
            (int)MathF.Floor(position.X), (int)MathF.Floor(position.Y), (int)MathF.Floor(position.Z),
            fluid);
        velocity += flow * (BlockFluid.FLOW_PUSH_PER_TICK / dt);

        velocity *= drag;
        return true;
    }

    // Runs once per game tick (not once per frame - see TickSystem). Applies gravity, moves the
    // entity while resolving collisions with blocks, and tracks fall damage/fire/footstep sounds.
    public virtual void Tick(World world)
    {
        float dt = TickSystem.TICK_DURATION; // fixed tick duration in seconds (not variable frame time)
        bool wasOnGround = IsOnGround;

        // 1) Fluid forces if submerged, otherwise gravity clamped to terminal velocity.
        if (!ApplyFluidForces(world, mPos, ref mVel))
        {
            mVel.Y -= Physics.GRAVITY * dt;
            mVel.Y = MathF.Max(mVel.Y, -Physics.TERMINAL_VEL);
        }

        // 2) Convert velocity (blocks/second) into this tick's displacement (blocks) and move,
        // resolving collisions against the world. `actual` may differ from the requested
        // `frameVelocity` if something blocked the path.
        float preCollisionVelY = mVel.Y;
        Vector3 frameVelocity = mVel * dt;
        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVelocity);
        mPos += actual;

        // If we tried to move down/up but actually moved much less than that, we hit something -
        // treat that as landing (or hitting a ceiling) and stop vertical velocity.
        if (MathF.Abs(actual.Y) < MathF.Abs(frameVelocity.Y) * 0.99f)
        {
            if (mVel.Y < 0)
                IsOnGround = true;
            mVel.Y = 0;
        }
        else
        {
            IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
        }

        // Just landed this tick - apply fall damage based on how far we fell, then reset the counter.
        if (IsOnGround && !wasOnGround)
        {
            if (mFallDistance > 0f)
            {
                Fall(world, mFallDistance);
                mFallDistance = 0f;
            }
        }
        else if (!IsOnGround && preCollisionVelY < 0f)
        {
            mFallDistance += -preCollisionVelY * dt;
        }

        if(hitFlashTimer > 0)
            hitFlashTimer -= dt;

        int fx = (int)MathF.Floor(mPos.X);
        int fy = (int)MathF.Floor(mPos.Y);
        int fz = (int)MathF.Floor(mPos.Z);
        var footBlock = world.GetBlock(fx, fy, fz);

        if (footBlock == BlockType.Water)
            mFallDistance = 0f;

        if (footBlock == BlockType.Fire)
            FireTimer = MathF.Max(FireTimer, 8f);

        if (FireTimer > 0f)
        {
            if (footBlock == BlockType.Water)
            {
                FireTimer = 0f;
                mFireDamageTimer = 0f;
            }
            else
            {
                FireTimer -= dt;
                mFireDamageTimer -= dt;
                if (mFireDamageTimer <= 0f)
                {
                    TakeDamage(1);
                    mFireDamageTimer = 1f;
                }
            }
        }

        float hSpeed = MathF.Sqrt(actual.X * actual.X + actual.Z * actual.Z) / dt;
        if (IsOnGround && hSpeed > 0.1f)
        {
            mStepTimer -= dt;
            if (mStepTimer <= 0f)
            {
                mStepTimer = STEP_INTERVAL;
                var bx = (int)MathF.Floor(mPos.X);
                var by = (int)MathF.Floor(mPos.Y - 0.05f);
                var bz = (int)MathF.Floor(mPos.Z);
                var stepBlock = world.GetBlock(bx, by, bz);
                var mat = BlockRegistry.GetBlockBreakMaterial(stepBlock);

                int volume = Proximity((ListenerPosition - this.Position).Length(), 20f, SfxVol);
                PlayStepSoundCallback?.Invoke(mat, volume);
                BlockRegistry.Get(stepBlock).OnEntityWalking(world, bx, by, bz, SharedRandom ?? new Random());
            }
        }
        else
        {
            mStepTimer = 0;
        }
    }

    public void Render(Matrix4x4 view, Matrix4x4 projection)
    {
        if (!IsAlive)
            return;

        int bx = (int)MathF.Floor(mPos.X);
        int by = (int)MathF.Floor(mPos.Y + Height * 0.5f);
        int bz = (int)MathF.Floor(mPos.Z);
        float skyLight = World.GetSkyLightGlobal(bx, by, bz) / (float)Terrain.Chunk.MAX_LIGHT;
        float blockLight = World.GetBlockLightGlobal(bx, by, bz) / (float)Terrain.Chunk.MAX_LIGHT;

        // Binds the entity shader and sets the uniforms shared by every part of this entity.
        // LightDir/AmbientStrength/SunlightLevel are frame constants the backend already holds.
        RenderBackend.Current.BeginEntity(GetFlashIntensity(), skyLight, blockLight);

        DrawModel(view, projection);

        if (IsOnFire)
            DrawFireBillboard(view, projection);
    }

    protected virtual void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        if (Model == null)
            return;

        Matrix4x4 mvp = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateTranslation(Position) * view * projection;
        DrawPart(Model, mvp);
    }

    protected static void DrawPart(IRenderHandle? model, Matrix4x4 mvp)
    {
        if (model == null)
            return;

        RenderBackend.Current.DrawModel(model, mvp);
    }

    private void DrawFireBillboard(Matrix4x4 view, Matrix4x4 projection)
    {
        float dx = CameraPosition.X - mPos.X;
        float dz = CameraPosition.Z - mPos.Z;
        float yaw = MathF.Atan2(dx, dz);

        Matrix4x4 mvp =
            Matrix4x4.CreateScale(Width * 1.5f, Height, 1f)
            * Matrix4x4.CreateRotationY(yaw)
            * Matrix4x4.CreateTranslation(mPos + new Vector3(0f, 0.3f, 0f))
            * view
            * projection;

        // The flame quad, its UVs, and the world atlas it samples all live on the backend now -
        // this side only supplies the transform.
        RenderBackend.Current.SetFloat("uHitFlash", 0f);
        RenderBackend.Current.DrawFireBillboard(mvp);
    }

    protected virtual void Fall(World world, float dist) { }

    public virtual void TakeDamage(int amount)
    {
        Health -= amount;
        CurrentAI?.OnHurt();

        hitFlashTimer = HIT_FLASH_DURATION;

        if (Health <= 0)
            IsAlive = false;
    }

    public virtual void Dispose() { }

    public int Proximity(float d, float maxDistance, int maxVolume) =>
        (int)(MathF.Pow(Math.Clamp(1f - d / maxDistance, 0f, 1f), 2f) * maxVolume);

    public float GetFlashIntensity()
    {
        return Math.Clamp(hitFlashTimer / HIT_FLASH_DURATION, 0, 1);
    }

    public static void DisposeShader() => RenderBackend.Current.DisposeEntityShader();

    public virtual Aabb GetBoundingBox()
    {
        float hw = Width / 2.0f;
        return new Aabb(new Vector3(mPos.X - hw, mPos.Y, mPos.Z - hw), new Vector3(mPos.X + hw, mPos.Y + Height, mPos.Z + hw));
    }

    // Ray-vs-box test ("slab method"): for each axis, find where the ray enters/exits the box's
    // range on that axis, then check whether all three axes overlap at once. Used for aiming at
    // entities (e.g. hitting a mob) the same way block raycasting picks a block.
    public bool IsLookedAt(Vector3 origin, Vector3 dir, float maxDist, out float dist)
    {
        dist = float.MaxValue;
        if (!IsAlive)
            return false;

        Aabb box = GetBoundingBox();
        Vector3 invDir = new(
            dir.X != 0 ? 1f / dir.X : float.MaxValue,
            dir.Y != 0 ? 1f / dir.Y : float.MaxValue,
            dir.Z != 0 ? 1f / dir.Z : float.MaxValue);

        float t1 = (box.Min.X - origin.X) * invDir.X;
        float t2 = (box.Max.X - origin.X) * invDir.X;
        float t3 = (box.Min.Y - origin.Y) * invDir.Y;
        float t4 = (box.Max.Y - origin.Y) * invDir.Y;
        float t5 = (box.Min.Z - origin.Z) * invDir.Z;
        float t6 = (box.Max.Z - origin.Z) * invDir.Z;

        float tmin = MathF.Max(MathF.Max(MathF.Min(t1, t2), MathF.Min(t3, t4)), MathF.Min(t5, t6));
        float tmax = MathF.Min(MathF.Min(MathF.Max(t1, t2), MathF.Max(t3, t4)), MathF.Max(t5, t6));

        if (tmax < 0 || tmin > tmax)
            return false;

        dist = tmin >= 0 ? tmin : tmax;
        return dist <= maxDist;
    }

    // Server-assigned ids come from their own counter, not sNextId. Both start at 1, so sharing one
    // would have a server id collide with whatever the client locally numbered its own entities -
    // and the client overwrites its local id with AssignNetworkId, so only this counter is
    // authoritative. Interlocked because entities are also constructed on generation workers.
    private static int sNextServerId;

    /// <summary>Allocates a network entity id. Server only - the client is told its ids.</summary>
    public static int AllocateId() => Interlocked.Increment(ref sNextServerId);
}
