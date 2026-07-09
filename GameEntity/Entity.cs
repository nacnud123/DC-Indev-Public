// Base entity class - position, velocity, physics, lighting, hit flash, fire, and model rendering | DA | 2/5/26 - 2/14/26
using Silk.NET.OpenGL;

using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Rendering;
using Shader = VoxelEngine.Rendering.Shader;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

// Base class for anything that moves around the world and isn't a block: the player, mobs (Pig, Zombie, Stalker...), dropped items, TNT, etc. Handles the stuff they all share - position/velocity, gravity and collision, taking fall/fire damage, and drawing a textured model. Subclasses override Tick/DrawModel for their own behavior and looks.
public class Entity
{
    // Single shared shader program used to render every entity's model (mobs, dropped items, arrows, etc.) - not per-instance, since they all use the same lit-textured vertex format.
    internal static Shader? _shader;
    private static bool _shaderInitialized;

    // Frame-constant lighting state - set each frame by GameRenderer before rendering
    public static Vector3 LightDir = new(-0.5f, -1f, -0.3f); // directional "sun" light direction, shared by all entities this frame
    public static float AmbientStrength = 0.4f;
    public static float SunlightLevel = 1f; // scales directional light by time-of-day/weather
    internal static Vector3 CameraPosition; // eye position for fire billboard yaw
    internal static Texture? SharedWorldTexture; // world atlas for fire billboard

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

    protected EntityModel? Model { get; set; }

    // Backing fields for Position/Velocity - kept private so subclasses go through the properties (mirrors the get/set pattern used for Health/Width/etc. above, though here it's a plain non-virtual property since position/velocity aren't meant to be overridden per-subclass).
    private Vector3 mPos;
    private Vector3 mVel;

    // World-space position in blocks. For most entities this is the *feet* position - the AABB (see GetBoundingBox) extends upward from here by Height, not centered on it.
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

    // Lazily compiles/loads the shared entity shader exactly once (idempotent - safe to call repeatedly). Must run after the GL context exists since shader compilation needs a live GL context.
    internal static void InitShader()
    {
        if (_shaderInitialized)
            return;
        _shader = new Shader(File.ReadAllText("Shaders/EntityVertex.glsl"), File.ReadAllText("Shaders/EntityFragment.glsl"));
        _shaderInitialized = true;
    }

    // Runs once per game tick (not once per frame - see TickSystem). Applies gravity, moves the entity while resolving collisions with blocks, and tracks fall damage/fire/footstep sounds.
    public virtual void Tick(World world)
    {
        float dt = TickSystem.TICK_DURATION; // fixed tick duration in seconds (not variable frame time)
        bool wasOnGround = IsOnGround;

        // 1) Apply gravity to vertical velocity, clamped to terminal velocity.
        mVel.Y -= Physics.GRAVITY * dt;
        mVel.Y = MathF.Max(mVel.Y, -Physics.TERMINAL_VEL);

        // 2) Convert velocity (blocks/second) into this tick's displacement (blocks) and move, resolving collisions against the world. `actual` may differ from the requested `frameVelocity` if something blocked the path.
        float preCollisionVelY = mVel.Y;
        Vector3 frameVelocity = mVel * dt;
        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVelocity);
        mPos += actual;

        // If we tried to move down/up but actually moved much less than that, we hit something - treat that as landing (or hitting a ceiling) and stop vertical velocity.
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

        _shader?.Use();
        _shader?.SetVector3("lightDir", LightDir);
        _shader?.SetFloat("ambientStrength", AmbientStrength);
        _shader?.SetFloat("uHitFlash", GetFlashIntensity());
        _shader?.SetFloat("sunlightLevel", SunlightLevel);
        _shader?.SetFloat("skyLight", skyLight);
        _shader?.SetFloat("blockLight", blockLight);

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

    protected static void DrawPart(EntityModel model, Matrix4x4 mvp)
    {
        _shader?.SetMatrix4("mvp", mvp);
        model.Texture.Use(TextureUnit.Texture0);
        GlContext.Gl.BindVertexArray(model.Vao);
        GlContext.Gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)model.VertexCount);
    }

    // Lazy-initialised fire billboard - one VAO shared across all entities
    private static uint _fireVao, _fireVbo;
    private static bool _fireVaoReady;

    private static void EnsureFireVao()
    {
        if (_fireVaoReady) return;

        var fireUv = UvHelper.FromTileCoords(6, 7);
        float u0 = fireUv.TopLeft.X,     v0 = fireUv.TopLeft.Y;
        float u1 = fireUv.BottomRight.X, v1 = fireUv.BottomRight.Y;

        // Vertex format must match the entity shader: pos(3) + uv(2) + normal(3) = 8 floats
        float[] verts =
        {
            -0.5f, 0f, 0f,  u0, v0,  0f, 0f, 1f,
             0.5f, 0f, 0f,  u1, v0,  0f, 0f, 1f,
             0.5f, 1f, 0f,  u1, v1,  0f, 0f, 1f,

            -0.5f, 0f, 0f,  u0, v0,  0f, 0f, 1f,
             0.5f, 1f, 0f,  u1, v1,  0f, 0f, 1f,
            -0.5f, 1f, 0f,  u0, v1,  0f, 0f, 1f,
        };

        var gl = GlContext.Gl;
        _fireVao = gl.GenVertexArray();
        _fireVbo = gl.GenBuffer();
        gl.BindVertexArray(_fireVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _fireVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts, BufferUsageARB.StaticDraw);

        uint stride = (uint)(8 * sizeof(float));
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, (nint)(5 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);
        _fireVaoReady = true;
    }

    private void DrawFireBillboard(Matrix4x4 view, Matrix4x4 projection)
    {
        EnsureFireVao();

        float dx = CameraPosition.X - mPos.X;
        float dz = CameraPosition.Z - mPos.Z;
        float yaw = MathF.Atan2(dx, dz);

        Matrix4x4 mvp =
            Matrix4x4.CreateScale(Width * 1.5f, Height, 1f)
            * Matrix4x4.CreateRotationY(yaw)
            * Matrix4x4.CreateTranslation(mPos + new Vector3(0f, 0.3f, 0f))
            * view
            * projection;

        _shader?.SetMatrix4("mvp", mvp);
        _shader?.SetFloat("uHitFlash", 0f);

        SharedWorldTexture?.Use(TextureUnit.Texture0);
        GlContext.Gl.BindVertexArray(_fireVao);
        GlContext.Gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
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

    public static void DisposeShader()
    {
        _shader?.Dispose();
        _shader = null;
        _shaderInitialized = false;

        if (_fireVaoReady)
        {
            var gl = GlContext.Gl;
            gl.DeleteVertexArray(_fireVao);
            gl.DeleteBuffer(_fireVbo);
            _fireVaoReady = false;
        }
    }

    public virtual Aabb GetBoundingBox()
    {
        float hw = Width / 2.0f;
        return new Aabb(new Vector3(mPos.X - hw, mPos.Y, mPos.Z - hw), new Vector3(mPos.X + hw, mPos.Y + Height, mPos.Z + hw));
    }

    // Ray-vs-box test ("slab method"): for each axis, find where the ray enters/exits the box's range on that axis, then check whether all three axes overlap at once. Used for aiming at entities (e.g. hitting a mob) the same way block raycasting picks a block.
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
}
