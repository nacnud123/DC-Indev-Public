// Main entity class, holds reference to entity model, position, and velocity. Along with other code related to Entities | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

public class Entity
{
    private static Shader? _shader;
    private static bool _shaderInitialized;

    public virtual int Health { get; set; } = 100;
    public virtual float Width { get; set; } = 0.6f;
    public virtual float Height { get; set; } = 1.8f;
    public virtual float EyeHeight { get; set; } = 1.62f;
    public virtual float WalkSpeed { get; set; } = 4.317f;
    public virtual float Scale { get; set; } = 1f;
    public virtual float JumpForce { get; set; }
    public float Yaw { get; set; }
    public bool IsOnGround { get; protected set; }
    public bool IsAlive { get; set; } = true;
    
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

    protected static void InitShader()
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
    }

    public virtual void Render(Matrix4 view, Matrix4 projection)
    {
        if (!IsAlive || Model == null) 
            return;

        Matrix4 model = Matrix4.CreateScale(Scale) * Matrix4.CreateRotationY(Yaw) * Matrix4.CreateTranslation(Position);
        Matrix4 mvp = model * view * projection;

        _shader?.Use();
        _shader?.SetMatrix4("mvp", mvp);
        Model.Texture.Use(TextureUnit.Texture0);
        GL.BindVertexArray(Model.Vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, Model.VertexCount);
    }

    public virtual void TakeDamage(int amount)
    {
        Health -= amount;
        CurrentAI?.OnHurt();

        if (Health <= 0)
            IsAlive = false;
    }

    public virtual void Dispose() { }

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
