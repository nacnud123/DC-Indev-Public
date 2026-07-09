// TNT block entity: falls with gravity, flashes white on a fuse, then explodes. Kind of like a block entity. | DA | 2/17/26

using Silk.NET.OpenGL;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

public class TntEntity : Entity
{
    private const float FLASH_INTERVAL_START = 0.5f; // how often it flashes right when the fuse starts (seconds)
    private const float FLASH_INTERVAL_END = 0.1f;   // how often it flashes right before exploding (seconds)
    private const float KNOCKBACK_STRENGTH = 30f;
    private const float FRICTION_COEFFICIENT = 0.85f; // slows horizontal sliding each tick, like ice friction

    private const int VERTEX_STRIDE = 8; // floats per vertex: 3 position + 2 texture coord + 3 normal

    private readonly Texture mWorldAtlas;
    private uint mVao, mVbo; // GPU handles for the cube's mesh data
    private int mVertexCount;

    private float mFuseTimer = 0f;   // counts down to 0, then it explodes
    private float inFuseTimer = 0f;  // the starting fuse length, kept so we can compute progress (t) in Tick

    private int inExplosionRad = 0;
    private float inExplosionPower = 0f;

    private float mFlashTimer;
    private bool mFlashOn;

    // The block that this entity is "standing in for" - only used to read its texture.
    private Block TextureBlock = null;

    public TntEntity(Vector3 position, Texture worldAtlas, Block parentBlock, float fuseDuration = 4f,
        int explosionRadius = 4, float explosionPower = 4.0f)
    {
        Position = position + new Vector3(0, 0.01f, 0);
        Width = 0.98f;
        Height = 0.98f;

        TextureBlock = parentBlock;

        inFuseTimer = fuseDuration;
        mFuseTimer = inFuseTimer;
        inExplosionRad = explosionRadius;
        inExplosionPower = explosionPower;
        
        mWorldAtlas = worldAtlas;
        BuildCubeMesh();
        Game.Instance?.AudioManager.PlayAudio("Resources/Audio/TNTHiss.ogg", Game.Instance.AudioManager.SfxVol);
    }

    // Builds a plain textured cube mesh by hand (2 triangles per face x 6 faces = 36 vertices) and uploads it to the GPU. This runs once when the TNT entity spawns, not every frame.
    private void BuildCubeMesh()
    {
        var top = TextureBlock.TopTextureCoords;
        var side = TextureBlock.SideTextureCoords;

        float u0t = top.TopLeft.X, v0t = top.TopLeft.Y, u1t = top.BottomRight.X, v1t = top.BottomRight.Y;
        float u0s = side.TopLeft.X, v0s = side.TopLeft.Y, u1s = side.BottomRight.X, v1s = side.BottomRight.Y;

        var verts = new List<float>();

        // Front (+Z)
        V(verts, 0, 0, 1, u0s, v0s, 0, 0, 1);
        V(verts, 1, 1, 1, u1s, v1s, 0, 0, 1);
        V(verts, 0, 1, 1, u0s, v1s, 0, 0, 1);
        V(verts, 0, 0, 1, u0s, v0s, 0, 0, 1);
        V(verts, 1, 0, 1, u1s, v0s, 0, 0, 1);
        V(verts, 1, 1, 1, u1s, v1s, 0, 0, 1);

        // Back (-Z)
        V(verts, 0, 0, 0, u1s, v0s, 0, 0, -1);
        V(verts, 0, 1, 0, u1s, v1s, 0, 0, -1);
        V(verts, 1, 1, 0, u0s, v1s, 0, 0, -1);
        V(verts, 0, 0, 0, u1s, v0s, 0, 0, -1);
        V(verts, 1, 1, 0, u0s, v1s, 0, 0, -1);
        V(verts, 1, 0, 0, u0s, v0s, 0, 0, -1);

        // Top (+Y)
        V(verts, 0, 1, 0, u0t, v0t, 0, 1, 0);
        V(verts, 1, 1, 1, u1t, v1t, 0, 1, 0);
        V(verts, 1, 1, 0, u1t, v0t, 0, 1, 0);
        V(verts, 0, 1, 0, u0t, v0t, 0, 1, 0);
        V(verts, 0, 1, 1, u0t, v1t, 0, 1, 0);
        V(verts, 1, 1, 1, u1t, v1t, 0, 1, 0);

        // Bottom (-Y)
        V(verts, 0, 0, 0, u0t, v1t, 0, -1, 0);
        V(verts, 1, 0, 0, u1t, v1t, 0, -1, 0);
        V(verts, 1, 0, 1, u1t, v0t, 0, -1, 0);
        V(verts, 0, 0, 0, u0t, v1t, 0, -1, 0);
        V(verts, 1, 0, 1, u1t, v0t, 0, -1, 0);
        V(verts, 0, 0, 1, u0t, v0t, 0, -1, 0);

        // Right (+X)
        V(verts, 1, 0, 0, u1s, v0s, 1, 0, 0);
        V(verts, 1, 1, 0, u1s, v1s, 1, 0, 0);
        V(verts, 1, 1, 1, u0s, v1s, 1, 0, 0);
        V(verts, 1, 0, 0, u1s, v0s, 1, 0, 0);
        V(verts, 1, 1, 1, u0s, v1s, 1, 0, 0);
        V(verts, 1, 0, 1, u0s, v0s, 1, 0, 0);

        // Left (-X)
        V(verts, 0, 0, 1, u1s, v0s, -1, 0, 0);
        V(verts, 0, 1, 1, u1s, v1s, -1, 0, 0);
        V(verts, 0, 1, 0, u0s, v1s, -1, 0, 0);
        V(verts, 0, 0, 1, u1s, v0s, -1, 0, 0);
        V(verts, 0, 1, 0, u0s, v1s, -1, 0, 0);
        V(verts, 0, 0, 0, u0s, v0s, -1, 0, 0);

        float[] arr = verts.ToArray();
        mVertexCount = arr.Length / VERTEX_STRIDE;

        var gl = GlContext.Gl;
        mVao = gl.GenVertexArray();
        mVbo = gl.GenBuffer();

        gl.BindVertexArray(mVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, arr, BufferUsageARB.StaticDraw);

        uint stride = (uint)(VERTEX_STRIDE * sizeof(float));
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, 3 * sizeof(float));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, 5 * sizeof(float));
        gl.EnableVertexAttribArray(2);

        gl.BindVertexArray(0);
    }

    // Appends one vertex: position (px,py,pz), texture coordinate (u,vv), and normal (nx,ny,nz).
    private static void V(List<float> v, float px, float py, float pz, float u, float vv, float nx, float ny, float nz)
    {
        v.Add(px);
        v.Add(py);
        v.Add(pz);
        v.Add(u);
        v.Add(vv);
        v.Add(nx);
        v.Add(ny);
        v.Add(nz);
    }

    public override void Tick(World world)
    {
        float dt = TickSystem.TICK_DURATION;

        mFuseTimer -= dt;

        // t goes from 0 (fuse just started) to 1 (about to explode), so the flash speeds up (flashInterval shrinks) the closer we get to exploding.
        float t = MathF.Max(0f, 1f - mFuseTimer / inFuseTimer);
        float flashInterval = (FLASH_INTERVAL_START + (FLASH_INTERVAL_END - FLASH_INTERVAL_START) * t);
        mFlashTimer -= dt;
        if (mFlashTimer <= 0f)
        {
            mFlashTimer = flashInterval;
            mFlashOn = !mFlashOn;
        }

        Velocity = new Vector3(
            Velocity.X * FRICTION_COEFFICIENT,
            Velocity.Y,
            Velocity.Z * FRICTION_COEFFICIENT
        );

        base.Tick(world);

        if (mFuseTimer <= 0f)
        {
            Explode(world);
            IsAlive = false;
        }
    }

    private void Explode(World world)
    {
        ExplosionUtil.Trigger(world, Position, inExplosionRad, inExplosionPower, KNOCKBACK_STRENGTH, this);
    }

    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        _shader?.SetFloat("uHitFlash", mFlashOn ? 0.85f : 0f);

        Matrix4x4 mvp = Matrix4x4.CreateTranslation(Position) * view * projection;
        _shader?.SetMatrix4("mvp", mvp);

        var gl = GlContext.Gl;
        mWorldAtlas.Use(TextureUnit.Texture0);
        gl.BindVertexArray(mVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mVertexCount);
    }

    public override void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVao);
        gl.DeleteBuffer(mVbo);
    }
}