// Base entity class — position, velocity, physics, lighting, hit flash, fire, and model rendering | DA | 2/5/26 - 2/14/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

public class Entity
{
    internal static Shader? _shader;
    private static bool _shaderInitialized;

    // Frame-constant lighting state — set each frame by GameRenderer before rendering
    public static Vector3 LightDir = new(-0.5f, -1f, -0.3f);
    public static float AmbientStrength = 0.4f;
    public static float SunlightLevel = 1f;
    internal static Vector3 CameraPosition; // eye position for fire billboard yaw
    internal static Texture? SharedWorldTexture; // world atlas for fire billboard

    // Tick-constant audio/game state — set by Game before ticking entities
    internal static Vector3 ListenerPosition; // player position for step-sound proximity
    internal static int SfxVol;
    internal static Action<Terrain.BlockBreakMaterial, int>? PlayStepSoundCallback;
    internal static Random? SharedRandom;

    public virtual bool IsTargetable => true;
    public virtual float ShadowSize => 0.5f;
    public virtual int Health { get; set; } = 100;
    public virtual float Width { get; set; } = 0.6f;
    public virtual float Height { get; set; } = 1.8f;
    public virtual float EyeHeight { get; set; } = 1.62f;
    public virtual float WalkSpeed { get; set; } = 4.317f;
    public virtual float SlowWalkSpeed { get; set; } = 2.1585f;
    public virtual float Scale { get; set; } = 1f;
    public virtual float JumpForce { get; set; }
    public float Yaw   { get; set; }
    public float Pitch { get; set; }
    public bool IsOnGround { get; protected set; }
    public bool IsAlive { get; set; } = true;
    private float hitFlashTimer = 0;
    const float HIT_FLASH_DURATION = 0.3f;
    private float mStepTimer;
    private const float STEP_INTERVAL = 0.5f;

    public float FireTimer { get; set; }
    public bool IsOnFire => FireTimer > 0f;
    private float mFireDamageTimer;
    protected float mFallDistance;

    protected EntityModel? Model { get; set; }

    private Vector3 mPos;
    private Vector3 mVel;

    public Vector3 Position
    {
        get => mPos;
        set => mPos = value;
    }
    
    public Vector3 Velocity
    {
        get => mVel;
        set => mVel = value;
    }

    public EntityAi? CurrentAI;

    internal static void InitShader()
    {
        if (_shaderInitialized)
            return;
        _shader = new Shader(File.ReadAllText("Shaders/EntityVertex.glsl"), File.ReadAllText("Shaders/EntityFragment.glsl"));
        _shaderInitialized = true;
    }

    public virtual void Tick(World world)
    {
        float dt = TickSystem.TICK_DURATION;
        bool wasOnGround = IsOnGround;

        mVel.Y -= Physics.GRAVITY * dt;
        mVel.Y = MathF.Max(mVel.Y, -Physics.TERMINAL_VEL);

        float preCollisionVelY = mVel.Y;
        Vector3 frameVelocity = mVel * dt;
        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVelocity);
        mPos += actual;

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

                int volume = Proximity((ListenerPosition - this.Position).Length, 20f, SfxVol);
                PlayStepSoundCallback?.Invoke(mat, volume);
                BlockRegistry.Get(stepBlock).OnEntityWalking(world, bx, by, bz, SharedRandom ?? new Random());
            }
        }
        else
        {
            mStepTimer = 0;
        }
    }

    public void Render(Matrix4 view, Matrix4 projection)
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

    protected virtual void DrawModel(Matrix4 view, Matrix4 projection)
    {
        if (Model == null)
            return;

        Matrix4 mvp = Matrix4.CreateScale(Scale) * Matrix4.CreateRotationY(Yaw) * Matrix4.CreateTranslation(Position) * view * projection;
        DrawPart(Model, mvp);
    }

    protected static void DrawPart(EntityModel model, Matrix4 mvp)
    {
        _shader?.SetMatrix4("mvp", mvp);
        model.Texture.Use(TextureUnit.Texture0);
        GL.BindVertexArray(model.Vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, model.VertexCount);
    }

    // Lazy-initialised fire billboard — one VAO shared across all entities
    private static int _fireVao, _fireVbo;
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

        _fireVao = GL.GenVertexArray();
        _fireVbo = GL.GenBuffer();
        GL.BindVertexArray(_fireVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _fireVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);

        int stride = 8 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.BindVertexArray(0);
        _fireVaoReady = true;
    }

    private void DrawFireBillboard(Matrix4 view, Matrix4 projection)
    {
        EnsureFireVao();

        float dx = CameraPosition.X - mPos.X;
        float dz = CameraPosition.Z - mPos.Z;
        float yaw = MathF.Atan2(dx, dz);

        Matrix4 mvp =
            Matrix4.CreateScale(Width * 1.5f, Height, 1f)
            * Matrix4.CreateRotationY(yaw)
            * Matrix4.CreateTranslation(mPos + new Vector3(0f, 0.3f, 0f))
            * view
            * projection;

        _shader?.SetMatrix4("mvp", mvp);
        _shader?.SetFloat("uHitFlash", 0f);

        SharedWorldTexture?.Use(TextureUnit.Texture0);
        GL.BindVertexArray(_fireVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
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
            GL.DeleteVertexArray(_fireVao);
            GL.DeleteBuffer(_fireVbo);
            _fireVaoReady = false;
        }
    }

    public virtual Aabb GetBoundingBox()
    {
        float hw = Width / 2.0f;
        return new Aabb(new Vector3(mPos.X - hw, mPos.Y, mPos.Z - hw), new Vector3(mPos.X + hw, mPos.Y + Height, mPos.Z + hw));
    }

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
