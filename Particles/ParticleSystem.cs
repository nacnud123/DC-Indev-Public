// Main class for particle system, holds reference to particle rendering and movement | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.Particles;

public class ParticleSystem : IDisposable
{
    private const int MAX_PARTICLES = 256;
    private const int QUAD_VERTEX_COUNT = 6;

    private readonly List<BlockParticle> mParticles = new();
    private readonly List<SmokeParticle> mSmokeParticles = new();
    private readonly int mVao;
    private readonly int mVbo;
    private readonly int mInstanceVbo;
    private readonly Shader mShader;
    private readonly Random mRandom = new();

    // Smoke rendering
    private readonly int mSmokeVao;
    private readonly int mSmokeInstanceVbo;
    private readonly Shader mSmokeShader;

    private static readonly float[] QuadVertices = {
        -0.5f, -0.5f, 0f,  0f, 0f,
         0.5f, -0.5f, 0f,  1f, 0f,
         0.5f,  0.5f, 0f,  1f, 1f,
         0.5f,  0.5f, 0f,  1f, 1f,
        -0.5f,  0.5f, 0f,  0f, 1f,
        -0.5f, -0.5f, 0f,  0f, 0f,
    };

    /*
    private const int CubeVertexCount = 36;
    private static readonly float[] CubeVertices = {
        // Front (Z+)
        -0.5f, -0.5f,  0.5f,  0f, 0f,
         0.5f, -0.5f,  0.5f,  1f, 0f,
         0.5f,  0.5f,  0.5f,  1f, 1f,
         0.5f,  0.5f,  0.5f,  1f, 1f,
        -0.5f,  0.5f,  0.5f,  0f, 1f,
        -0.5f, -0.5f,  0.5f,  0f, 0f,
        // Back (Z-)
         0.5f, -0.5f, -0.5f,  0f, 0f,
        -0.5f, -0.5f, -0.5f,  1f, 0f,
        -0.5f,  0.5f, -0.5f,  1f, 1f,
        -0.5f,  0.5f, -0.5f,  1f, 1f,
         0.5f,  0.5f, -0.5f,  0f, 1f,
         0.5f, -0.5f, -0.5f,  0f, 0f,
        // Top (Y+)
        -0.5f,  0.5f,  0.5f,  0f, 0f,
         0.5f,  0.5f,  0.5f,  1f, 0f,
         0.5f,  0.5f, -0.5f,  1f, 1f,
         0.5f,  0.5f, -0.5f,  1f, 1f,
        -0.5f,  0.5f, -0.5f,  0f, 1f,
        -0.5f,  0.5f,  0.5f,  0f, 0f,
        // Bottom (Y-)
        -0.5f, -0.5f, -0.5f,  0f, 0f,
         0.5f, -0.5f, -0.5f,  1f, 0f,
         0.5f, -0.5f,  0.5f,  1f, 1f,
         0.5f, -0.5f,  0.5f,  1f, 1f,
        -0.5f, -0.5f,  0.5f,  0f, 1f,
        -0.5f, -0.5f, -0.5f,  0f, 0f,
        // Right (X+)
         0.5f, -0.5f,  0.5f,  0f, 0f,
         0.5f, -0.5f, -0.5f,  1f, 0f,
         0.5f,  0.5f, -0.5f,  1f, 1f,
         0.5f,  0.5f, -0.5f,  1f, 1f,
         0.5f,  0.5f,  0.5f,  0f, 1f,
         0.5f, -0.5f,  0.5f,  0f, 0f,
        // Left (X-)
        -0.5f, -0.5f, -0.5f,  0f, 0f,
        -0.5f, -0.5f,  0.5f,  1f, 0f,
        -0.5f,  0.5f,  0.5f,  1f, 1f,
        -0.5f,  0.5f,  0.5f,  1f, 1f,
        -0.5f,  0.5f, -0.5f,  0f, 1f,
        -0.5f, -0.5f, -0.5f,  0f, 0f,
    };
    */

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceData
    {
        public Matrix4 ModelMatrix;
        public Vector4 UVRegion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmokeInstanceData
    {
        public Matrix4 ModelMatrix;
        public float Alpha;
    }

    public ParticleSystem()
    {
        mShader = new Shader( File.ReadAllText("Shaders/ParticleVert.glsl"), File.ReadAllText("Shaders/ParticleFrag.glsl"));

        mVao = GL.GenVertexArray();
        GL.BindVertexArray(mVao);

        mVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, QuadVertices.Length * sizeof(float), QuadVertices, BufferUsageHint.StaticDraw);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        mInstanceVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, mInstanceVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, MAX_PARTICLES * Marshal.SizeOf<InstanceData>(), IntPtr.Zero, BufferUsageHint.DynamicDraw);

        SetupInstanceAttributes();

        GL.BindVertexArray(0);

        // Smoke setup
        mSmokeShader = new Shader(File.ReadAllText("Shaders/SmokeVert.glsl"), File.ReadAllText("Shaders/SmokeFrag.glsl"));

        mSmokeVao = GL.GenVertexArray();
        GL.BindVertexArray(mSmokeVao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo); // Reuse quad vertices

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        mSmokeInstanceVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, mSmokeInstanceVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, MAX_PARTICLES * Marshal.SizeOf<SmokeInstanceData>(), IntPtr.Zero, BufferUsageHint.DynamicDraw);

        SetupSmokeInstanceAttributes();

        GL.BindVertexArray(0);
    }

    private void SetupInstanceAttributes()
    {
        int stride = Marshal.SizeOf<InstanceData>();

        for (int i = 0; i < 4; i++)
        {
            GL.VertexAttribPointer(2 + i, 4, VertexAttribPointerType.Float, false, stride, i * 16);
            GL.EnableVertexAttribArray(2 + i);
            GL.VertexAttribDivisor(2 + i, 1);
        }

        GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, stride, 64);
        GL.EnableVertexAttribArray(6);
        GL.VertexAttribDivisor(6, 1);
    }

    private void SetupSmokeInstanceAttributes()
    {
        int stride = Marshal.SizeOf<SmokeInstanceData>();

        // Matrix4
        for (int i = 0; i < 4; i++)
        {
            GL.VertexAttribPointer(2 + i, 4, VertexAttribPointerType.Float, false, stride, i * 16);
            GL.EnableVertexAttribArray(2 + i);
            GL.VertexAttribDivisor(2 + i, 1);
        }

        // Alpha
        GL.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, false, stride, 64);
        GL.EnableVertexAttribArray(6);
        GL.VertexAttribDivisor(6, 1);
    }

    public void SpawnBlockBreakParticles(Vector3 blockPos, BlockType type)
    {
        var blockUv = BlockRegistry.GetParticleTexture(type);
        int count = mRandom.Next(10, 20);

        for (int i = 0; i < count; i++)
        {
            var particleUv = UvHelper.GetRandomSubTile(blockUv, mRandom);

            mParticles.Add(new BlockParticle
            {
                Pos = blockPos + new Vector3(RandomRange(0.2f, 0.8f), RandomRange(0.2f, 0.8f), RandomRange(0.2f, 0.8f)),
                Vel = new Vector3(RandomRange(-2f, 2f), RandomRange(1f, 4f), RandomRange(-2f, 2f)),
                UvOffset = particleUv.TopLeft,
                UvSize = particleUv.BottomRight - particleUv.TopLeft,
                Size = RandomRange(0.05f, 0.15f),
                Lifetime = RandomRange(0.5f, 1.5f),
                Gravity = 14f
            });
        }
    }

    public void SpawnSmokeParticle(Vector3 position)
    {
        float lifetime = RandomRange(1.0f, 2.0f);
        mSmokeParticles.Add(new SmokeParticle
        {
            Pos = position + new Vector3(0.5f, 0.7f, 0.5f),
            Vel = new Vector3(RandomRange(-0.2f, 0.2f), RandomRange(0.5f, 1.0f), RandomRange(-0.2f, 0.2f)),
            Lifetime = lifetime,
            MaxLifetime = lifetime,
            Size = RandomRange(0.05f, 0.1f),
            Gravity = 0.1f
        });
    }

    public void Update(float deltaTime, World world)
    {
        for (int i = mParticles.Count - 1; i >= 0; i--)
        {
            var p = mParticles[i];

            p.Vel.Y -= p.Gravity * deltaTime;
            var newPos = p.Pos + p.Vel * deltaTime;

            int bx = (int)MathF.Floor(newPos.X);
            int by = (int)MathF.Floor(newPos.Y);
            int bz = (int)MathF.Floor(newPos.Z);

            if (world.GetBlock(bx, by, bz) != BlockType.Air)
                p.Vel = Vector3.Zero;
            else
                p.Pos = newPos;

            p.Lifetime -= deltaTime;

            if (p.Lifetime <= 0)
                mParticles.RemoveAt(i);
            else
                mParticles[i] = p;
        }
    }

    public void UpdateSmoke(float deltaTime, World world)
    {
        for (int i = mSmokeParticles.Count - 1; i >= 0; i--)
        {
            var p = mSmokeParticles[i];
            p.Pos += p.Vel * deltaTime;
            p.Vel.Y += 0.1f * deltaTime;
            p.Lifetime -= deltaTime;


            if (p.Lifetime <= 0)
                mSmokeParticles.RemoveAt(i);
            else
                mSmokeParticles[i] = p;
        }
    }

    public void Render(Matrix4 view, Matrix4 projection, Texture worldTexture)
    {
        if (mParticles.Count == 0)
            return;

        var instances = new InstanceData[mParticles.Count];

        for (int i = 0; i < mParticles.Count; i++)
        {
            var p = mParticles[i];
            instances[i] = new InstanceData
            {
                ModelMatrix = Matrix4.CreateScale(p.Size) * Matrix4.CreateTranslation(p.Pos),
                UVRegion = new Vector4(p.UvOffset.X, p.UvOffset.Y, p.UvSize.X, p.UvSize.Y)
            };
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, mInstanceVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, instances.Length * Marshal.SizeOf<InstanceData>(), instances);

        mShader.Use();
        mShader.SetMatrix4("view", view);
        mShader.SetMatrix4("projection", projection);
        mShader.SetInt("blockTexture", 0);

        worldTexture.Use(TextureUnit.Texture0);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        GL.BindVertexArray(mVao);
        GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, QUAD_VERTEX_COUNT, mParticles.Count);
        GL.BindVertexArray(0);

        GL.Disable(EnableCap.Blend);
    }

    public void RenderSmoke(Matrix4 view, Matrix4 projection)
    {
        if (mSmokeParticles.Count == 0)
            return;

        var instances = new SmokeInstanceData[mSmokeParticles.Count];

        for (int i = 0; i < mSmokeParticles.Count; i++)
        {
            var p = mSmokeParticles[i];
            instances[i] = new SmokeInstanceData
            {
                ModelMatrix = Matrix4.CreateScale(p.Size) * Matrix4.CreateTranslation(p.Pos),
                Alpha = p.Lifetime / p.MaxLifetime
            };
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, mSmokeInstanceVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, instances.Length * Marshal.SizeOf<SmokeInstanceData>(), instances);

        mSmokeShader.Use();
        mSmokeShader.SetMatrix4("view", view);
        mSmokeShader.SetMatrix4("projection", projection);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        GL.BindVertexArray(mSmokeVao);
        GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, QUAD_VERTEX_COUNT, mSmokeParticles.Count);
        GL.BindVertexArray(0);

        GL.Disable(EnableCap.Blend);
    }

    private float RandomRange(float min, float max) => (float)mRandom.NextDouble() * (max - min) + min;

    public void Dispose()
    {
        GL.DeleteVertexArray(mVao);
        GL.DeleteBuffer(mVbo);
        GL.DeleteBuffer(mInstanceVbo);
        mShader?.Dispose();

        GL.DeleteVertexArray(mSmokeVao);
        GL.DeleteBuffer(mSmokeInstanceVbo);
        mSmokeShader?.Dispose();
    }
}
