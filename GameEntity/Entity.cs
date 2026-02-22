// Main entity class, holds reference to entity model, position, and velocity. Along with other code related to Entities | DA | 2/5/26 - 2/14/26
// Updated with lighting, now entities will become darker or lighter depending on the lighting. Also, added functions to draw certain body parts. Finally, added in audio integration.
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

public class Entity
{
    internal static Shader? _shader;
    private static bool _shaderInitialized;

    public static Vector3 LightDir = new(-0.5f, -1f, -0.3f);
    public static float AmbientStrength = 0.4f;
    public static float SunlightLevel = 1f;

    public virtual int Health { get; set; } = 100;
    public virtual float Width { get; set; } = 0.6f;
    public virtual float Height { get; set; } = 1.8f;
    public virtual float EyeHeight { get; set; } = 1.62f;
    public virtual float WalkSpeed { get; set; } = 4.317f;
    public virtual float SlowWalkSpeed { get; set; } = 2.1585f;
    public virtual float Scale { get; set; } = 1f;
    public virtual float JumpForce { get; set; }
    public float Yaw { get; set; }
    public bool IsOnGround { get; protected set; }
    public bool IsAlive { get; set; } = true;
    private float hitFlashTimer = 0;
    const float HIT_FLASH_DURATION = 0.3f;
    private float mStepTimer;
    private const float STEP_INTERVAL = 0.5f;

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

        mVel.Y -= Physics.GRAVITY * dt;
        mVel.Y = MathF.Max(mVel.Y, -Physics.TERMINAL_VEL);

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

        if(hitFlashTimer > 0)
            hitFlashTimer -= dt;

        // Footstep sounds
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
                var mat = BlockRegistry.GetBlockBreakMaterial(world.GetBlock(bx, by, bz));

                int volume = Proximity((Game.Instance.GetPlayer.Position - this.Position).Length ,20f, Game.Instance.AudioManager.SfxVol);
                
                Game.Instance.AudioManager.PlayBlockContactSound(mat, volume);
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
    }

    public Aabb GetBoundingBox()
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
