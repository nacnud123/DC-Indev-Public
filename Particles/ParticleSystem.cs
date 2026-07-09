// The main class for the particle system holds reference to particle rendering and movement | DA | 2/5/26 Both smoke and regular particles use instance rendering, making there is one draw call that renders all particles of the type.
using Silk.NET.OpenGL;

using System.Runtime.InteropServices;
using VoxelEngine.Rendering;
using Shader = VoxelEngine.Rendering.Shader;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.Particles;

/// <summary>
/// Owns GPU resources and simulation state for the two particle effect types (block-break debris and smoke puffs), and draws them using GPU instancing - one draw call renders every live particle of a given type as a camera-facing quad, with per-particle transform/UV/alpha supplied via a per-instance vertex buffer rather than one draw call per particle. Lifecycle: Spawn* methods add new particles; Update/UpdateSmoke integrate simple physics (gravity, water drag, ground collision) each tick and cull expired ones; Render/RenderSmoke upload the current particle state to the instance buffer and issue the instanced draw call. In the overall frame render order (see Core/Game.cs) particles are drawn after entities and paintings, but before the block highlight outline and HUD.
/// </summary>
public class ParticleSystem : IDisposable
{
    // Fixed capacity for each particle type's instance buffer - both the CPU-side array and the GPU buffer are sized to this; spawning beyond it is either ignored (smoke) or simply not rendered/updated for the overflow (block particles are capped only at render time via Math.Min(mParticles.Count, MAX_PARTICLES)).
    private const int MAX_PARTICLES = 256;
    // 6 vertices (2 triangles) forming the flat quad each particle instance is drawn as.
    private const int QUAD_VERTEX_COUNT = 6;

    private readonly List<BlockParticle> mParticles = new();
    private readonly List<SmokeParticle> mSmokeParticles = new();
    private readonly InstanceData[] mInstanceBuffer = new InstanceData[MAX_PARTICLES];
    private readonly SmokeInstanceData[] mSmokeInstanceBuffer = new SmokeInstanceData[MAX_PARTICLES];
    private readonly uint mVao;
    private readonly uint mVbo;
    private readonly uint mInstanceVbo;
    private readonly Shader mShader;
    private readonly Random mRandom = new();

    // Smoke rendering
    private readonly uint mSmokeVao;
    private readonly uint mSmokeInstanceVbo;
    private readonly Shader mSmokeShader;

    // Vertices for the particle, it's a flat square
    private static readonly float[] QuadVertices = {
        -0.5f, -0.5f, 0f,  0f, 0f,
         0.5f, -0.5f, 0f,  1f, 0f,
         0.5f,  0.5f, 0f,  1f, 1f,
         0.5f,  0.5f, 0f,  1f, 1f,
        -0.5f,  0.5f, 0f,  0f, 1f,
        -0.5f, -0.5f, 0f,  0f, 0f,
    };

    // [StructLayout(LayoutKind.Sequential)] guarantees the field order/packing here matches memory layout exactly, since this struct is uploaded directly as raw bytes to a GL buffer (via BufferSubData<T>) and read back by the vertex shader through the VertexAttribPointer offsets configured in SetupInstanceAttributes.
    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceData
    {
        // Per-particle model matrix (scale * translation) transforming the shared unit quad into the particle's world position/size. Occupies 64 bytes (4x Vector4 rows), consumed by shader attributes 2-5, one per matrix row.
        public Matrix4x4 ModelMatrix;
        // (uvOffset.xy, uvSize.xy) packed into one vec4 - the sub-tile texture region (see UvHelper.GetRandomSubTile) this particle instance samples from.
        public Vector4 UVRegion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmokeInstanceData
    {
        public Matrix4x4 ModelMatrix;
        // Fade-out opacity for this smoke instance (Lifetime / MaxLifetime), consumed by the smoke fragment shader instead of a texture UV region.
        public float Alpha;
    }

    // Init, load shader, set up GL stuff
    public ParticleSystem()
    {
        var gl = GlContext.Gl;
        mShader = new Shader(File.ReadAllText("Shaders/ParticleVert.glsl"), File.ReadAllText("Shaders/ParticleFrag.glsl"));

        mVao = gl.GenVertexArray();
        gl.BindVertexArray(mVao);

        // Shared unit-quad geometry (position.xyz + uv.xy per vertex, 5 floats/vertex), uploaded once as static data - all particle instances reuse this same quad and differ only via their per-instance attributes (model matrix, UV region/alpha).
        mVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, QuadVertices, BufferUsageARB.StaticDraw);

        // Attribute 0: vertex position (3 floats), stride = 5 floats/vertex, offset 0.
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(5 * sizeof(float)), 0);
        gl.EnableVertexAttribArray(0);

        // Attribute 1: vertex UV (2 floats), offset 3 floats in (after the position).
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(5 * sizeof(float)), (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);

        // Per-instance buffer: allocated up-front at MAX_PARTICLES capacity with no initial data (null pointer + DynamicDraw hint since it's rewritten every frame via BufferSubData in Render()). The `null` pointer is a valid GL usage for BufferData - it just reserves GPU storage without an initial upload - so this call is technically unsafe-context only because Silk.NET's signature takes a raw pointer, not because dereferencing anything unsafe happens here.
        mInstanceVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mInstanceVbo);
        unsafe
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MAX_PARTICLES * Marshal.SizeOf<InstanceData>()), null, BufferUsageARB.DynamicDraw);
        }

        SetupInstanceAttributes(gl);

        gl.BindVertexArray(0);

        // Smoke setup
        mSmokeShader = new Shader(File.ReadAllText("Shaders/SmokeVert.glsl"), File.ReadAllText("Shaders/SmokeFrag.glsl"));

        mSmokeVao = gl.GenVertexArray();
        gl.BindVertexArray(mSmokeVao);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo); // Reuse quad vertices

        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(5 * sizeof(float)), 0);
        gl.EnableVertexAttribArray(0);

        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(5 * sizeof(float)), (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);

        // Same "reserve GPU storage with null data" pattern as the block-particle instance buffer above, sized for SmokeInstanceData instead.
        mSmokeInstanceVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mSmokeInstanceVbo);
        unsafe
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MAX_PARTICLES * Marshal.SizeOf<SmokeInstanceData>()), null, BufferUsageARB.DynamicDraw);
        }

        SetupSmokeInstanceAttributes(gl);

        gl.BindVertexArray(0);
    }

    // Set up the regular particle instance attributes. A Matrix4x4 can't be passed as a single vertex attribute (GL attributes max out at 4 components/vec4), so it's split across 4 consecutive attribute slots (2,3,4,5), one per matrix row, each a vec4 at a 16-byte (4-float) offset within the InstanceData struct. VertexAttribDivisor(attr, 1) is what makes these "per-instance" rather than "per-vertex" - the attribute advances once per particle instance drawn, instead of once per vertex of the shared quad.
    private void SetupInstanceAttributes(GL gl)
    {
        uint stride = (uint)Marshal.SizeOf<InstanceData>();

        // Matrix4x4 rows -> attributes 2,3,4,5 (16 bytes = 4 floats apart per row).
        for (uint i = 0; i < 4; i++)
        {
            gl.VertexAttribPointer(2 + i, 4, GLEnum.Float, false, stride, (nint)(i * 16));
            gl.EnableVertexAttribArray(2 + i);
            gl.VertexAttribDivisor(2 + i, 1);
        }
        // UVRegion (vec4) -> attribute 6, located right after the 64-byte (4x16) matrix.
        gl.VertexAttribPointer(6, 4, GLEnum.Float, false, stride, 64);
        gl.EnableVertexAttribArray(6);
        gl.VertexAttribDivisor(6, 1);
    }

    // Set up the smoke particle instance attributes. Same per-row matrix splitting as SetupInstanceAttributes, but attribute 6 is a single float (Alpha) instead of a vec4 UV region.
    private void SetupSmokeInstanceAttributes(GL gl)
    {
        uint stride = (uint)Marshal.SizeOf<SmokeInstanceData>();

        // Matrix4x4 rows -> attributes 2,3,4,5.
        for (uint i = 0; i < 4; i++)
        {
            gl.VertexAttribPointer(2 + i, 4, GLEnum.Float, false, stride, (nint)(i * 16));
            gl.EnableVertexAttribArray(2 + i);
            gl.VertexAttribDivisor(2 + i, 1);
        }

        // Alpha (single float) -> attribute 6, right after the 64-byte matrix.
        gl.VertexAttribPointer(6, 1, GLEnum.Float, false, stride, 64);
        gl.EnableVertexAttribArray(6);
        gl.VertexAttribDivisor(6, 1);
    }

    /// <summary>
    /// Spawns a burst of 10-19 debris particles at a broken block's position, each textured with a random small sub-tile sampled from that block type's particle texture (see BlockRegistry.GetParticleTexture / UvHelper.GetRandomSubTile) so the debris visually resembles chunks of the block's own texture.
    /// </summary>
    public void SpawnBlockBreakParticles(Vector3 blockPos, BlockType type)
    {
        var blockUv = BlockRegistry.GetParticleTexture(type);
        int count = mRandom.Next(10, 20);

        for (int i = 0; i < count; i++)
        {
            var particleUv = UvHelper.GetRandomSubTile(blockUv, mRandom);

            mParticles.Add(new BlockParticle
            {
                // Spawn somewhere within the inner 60% of the block's 1x1x1 volume (0.2-0.8 on each axis) so particles don't appear to originate exactly on the block's edges.
                Pos = blockPos + new Vector3(RandomRange(0.2f, 0.8f), RandomRange(0.2f, 0.8f), RandomRange(0.2f, 0.8f)),
                // Random outward/upward "pop" velocity so particles scatter visibly.
                Vel = new Vector3(RandomRange(-2f, 2f), RandomRange(1f, 4f), RandomRange(-2f, 2f)),
                UvOffset = particleUv.TopLeft,
                UvSize = particleUv.BottomRight - particleUv.TopLeft,
                Size = RandomRange(0.05f, 0.15f),
                Lifetime = RandomRange(0.5f, 1.5f),
                Gravity = 14f
            });
        }
    }

    /// <summary>
    /// Spawns a single smoke puff (e.g. from a lit furnace/fire). Silently no-ops once the smoke pool is at MAX_PARTICLES capacity rather than growing unbounded.
    /// </summary>
    public void SpawnSmokeParticle(Vector3 position)
    {
        if (mSmokeParticles.Count >= MAX_PARTICLES)
            return;

        float lifetime = RandomRange(1.0f, 2.0f);
        mSmokeParticles.Add(new SmokeParticle
        {
            // Offset upward/centered from the block origin (0.5, 0.7, 0.5) so smoke appears to rise from roughly the top-center of the source block.
            Pos = position + new Vector3(0.5f, 0.7f, 0.5f),
            Vel = new Vector3(RandomRange(-0.2f, 0.2f), RandomRange(0.5f, 1.0f), RandomRange(-0.2f, 0.2f)),
            Lifetime = lifetime,
            MaxLifetime = lifetime,
            Size = RandomRange(0.05f, 0.1f),
            Gravity = 0.1f
        });
    }

    /// <summary>
    /// Advances all live block-break particles by one tick: applies gravity (reduced/damped when submerged in water), moves them, and resolves a very simple collision - if the next position would land inside a solid (non-air, non-water) block, velocity is zeroed (particle "sticks"/stops) instead of moving into it. Expired particles are removed via swap-with-last for O(1) removal (order doesn't matter for particles, so this avoids an O(n) shift).
    /// </summary>
    public void Update(float deltaTime, World world)
    {
        for (int i = mParticles.Count - 1; i >= 0; i--)
        {
            var p = mParticles[i];

            // Floor the position to get the containing block's integer coordinates (world-space block grid), then check if it's water to apply buoyancy-like damping.
            bool inWater = world.GetBlock((int)MathF.Floor(p.Pos.X), (int)MathF.Floor(p.Pos.Y), (int)MathF.Floor(p.Pos.Z)) == BlockType.Water;
            // Gravity is heavily reduced (15%) underwater so particles sink slowly instead of plummeting.
            float gravity = inWater ? p.Gravity * 0.15f : p.Gravity;

            p.Vel.Y -= gravity * deltaTime;

            if (inWater)
                // Exponential drag: velocity decays toward zero at a rate independent of frame rate (0.6^(deltaTime*20) approaches 1 as deltaTime->0 and shrinks faster for larger steps), simulating water resistance.
                p.Vel *= MathF.Pow(0.6f, deltaTime * 20f);

            var newPos = p.Pos + p.Vel * deltaTime;

            int bx = (int)MathF.Floor(newPos.X);
            int by = (int)MathF.Floor(newPos.Y);
            int bz = (int)MathF.Floor(newPos.Z);
            var blockAtNew = world.GetBlock(bx, by, bz);

            if (blockAtNew != BlockType.Air && blockAtNew != BlockType.Water)
                // Naive collision response: rather than sliding/bouncing, the particle simply stops dead (velocity zeroed) and stays at its last valid position.
                p.Vel = Vector3.Zero;
            else
                p.Pos = newPos;

            p.Lifetime -= deltaTime;

            if (p.Lifetime <= 0)
            {
                // Swap-remove: overwrite the expired particle with the last one in the list and shrink by one, avoiding an O(n) shift from RemoveAt(i).
                mParticles[i] = mParticles[mParticles.Count - 1];
                mParticles.RemoveAt(mParticles.Count - 1);
            }
            else
                mParticles[i] = p;
        }
    }

    /// <summary>
    /// Advances all live smoke particles: simple upward drift (velocity gains a small constant upward acceleration) plus straight-line movement, no collision against the world (smoke passes through blocks). Expired particles are removed via the same swap-with-last trick as Update().
    /// </summary>
    public void UpdateSmoke(float deltaTime, World world)
    {
        for (int i = mSmokeParticles.Count - 1; i >= 0; i--)
        {
            var p = mSmokeParticles[i];
            p.Pos += p.Vel * deltaTime;
            p.Vel.Y += 0.1f * deltaTime;
            p.Lifetime -= deltaTime;

            if (p.Lifetime <= 0)
            {
                mSmokeParticles[i] = mSmokeParticles[mSmokeParticles.Count - 1];
                mSmokeParticles.RemoveAt(mSmokeParticles.Count - 1);
            }
            else
                mSmokeParticles[i] = p;
        }
    }

    /// <summary>
    /// Uploads current block-break particle state into the instance buffer and issues a single instanced draw call for all live particles (up to MAX_PARTICLES). The model matrix here is deliberately NOT billboarded toward the camera (unlike some particle systems) - it's a plain scale-then-translate, so the quad's facing is fixed rather than rotating to face the viewer; the vertex/fragment shader (ParticleVert/FragShader) is responsible for any additional camera-facing behavior if present.
    /// </summary>
    public void Render(Matrix4x4 view, Matrix4x4 projection, Texture worldTexture)
    {
        if (mParticles.Count == 0)
            return;

        // Only render up to buffer capacity even if more particles exist logically (shouldn't normally happen since spawn count is small, but guards the buffer).
        int renderCount = Math.Min(mParticles.Count, MAX_PARTICLES);

        for (int i = 0; i < renderCount; i++)
        {
            var p = mParticles[i];
            // Order matters: CreateScale then CreateTranslation (matrices compose right-to-left applied to a vector) scales the unit quad to Size first, then moves it to the particle's world position.
            mInstanceBuffer[i] = new InstanceData
            {
                ModelMatrix = Matrix4x4.CreateScale(p.Size) * Matrix4x4.CreateTranslation(p.Pos),
                UVRegion = new Vector4(p.UvOffset.X, p.UvOffset.Y, p.UvSize.X, p.UvSize.Y)
            };
        }

        var gl = GlContext.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mInstanceVbo);
        // Upload only the live portion of the CPU-side instance array (not the full MAX_PARTICLES-sized buffer) directly into the GPU buffer allocated earlier.
        gl.BufferSubData<InstanceData>(BufferTargetARB.ArrayBuffer, 0,
            mInstanceBuffer.AsSpan(0, renderCount));

        mShader.Use();
        mShader.SetMatrix4("view", view);
        mShader.SetMatrix4("projection", projection);
        mShader.SetInt("blockTexture", 0);

        worldTexture.Use(TextureUnit.Texture0);

        // Alpha blending so particle edges/any semi-transparent texels composite correctly against the scene rather than being hard-edged.
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        gl.BindVertexArray(mVao);
        // One draw call renders QUAD_VERTEX_COUNT vertices x renderCount instances, using the per-instance attributes (divisor 1) set up in SetupInstanceAttributes.
        gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, QUAD_VERTEX_COUNT, (uint)renderCount);
        gl.BindVertexArray(0);

        gl.Disable(EnableCap.Blend);
    }

    /// <summary>
    /// Same pattern as Render() but for smoke particles: uploads instance data (model matrix + fade alpha) and issues one instanced draw call.
    /// </summary>
    public void RenderSmoke(Matrix4x4 view, Matrix4x4 projection)
    {
        if (mSmokeParticles.Count == 0)
            return;

        int renderCount = Math.Min(mSmokeParticles.Count, MAX_PARTICLES);

        for (int i = 0; i < renderCount; i++)
        {
            var p = mSmokeParticles[i];
            mSmokeInstanceBuffer[i] = new SmokeInstanceData
            {
                ModelMatrix = Matrix4x4.CreateScale(p.Size) * Matrix4x4.CreateTranslation(p.Pos),
                // Fade out linearly as the particle approaches the end of its life (1 = fresh/opaque, 0 = about to expire).
                Alpha = p.Lifetime / p.MaxLifetime
            };
        }

        var gl = GlContext.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mSmokeInstanceVbo);
        gl.BufferSubData<SmokeInstanceData>(BufferTargetARB.ArrayBuffer, 0,
            mSmokeInstanceBuffer.AsSpan(0, renderCount));

        mSmokeShader.Use();
        mSmokeShader.SetMatrix4("view", view);
        mSmokeShader.SetMatrix4("projection", projection);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        gl.BindVertexArray(mSmokeVao);
        gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, QUAD_VERTEX_COUNT, (uint)renderCount);
        gl.BindVertexArray(0);

        gl.Disable(EnableCap.Blend);
    }

    // Uniformly distributed random float in [min, max).
    private float RandomRange(float min, float max) => (float)mRandom.NextDouble() * (max - min) + min;

    /// <summary>Releases all GL objects (VAOs/VBOs/shaders) owned by this system for both particle types.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVao);
        gl.DeleteBuffer(mVbo);
        gl.DeleteBuffer(mInstanceVbo);
        mShader?.Dispose();

        gl.DeleteVertexArray(mSmokeVao);
        gl.DeleteBuffer(mSmokeInstanceVbo);
        mSmokeShader?.Dispose();
    }
}
