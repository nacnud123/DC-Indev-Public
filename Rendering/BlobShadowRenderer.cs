// Blob shadow: a gradient disc projected onto the ground below each entity. | DA | 3/10/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.GameEntity;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Rendering;

public class BlobShadowRenderer : IDisposable
{
    private const int   DISC_SEGMENTS = 16;
    private const float FADE_DISTANCE = 64f;
    private const float MAX_DROP = 6f;
    private const float Y_BIAS = 0.004f;

    private readonly Shader mShader;
    private readonly int mVao;
    private readonly int mVbo;
    private readonly int mVertexCount;

    public BlobShadowRenderer()
    {
        mShader = new Shader(File.ReadAllText("Shaders/ShadowDiscVert.glsl"), File.ReadAllText("Shaders/ShadowDiscFrag.glsl"));

        (mVao, mVbo, mVertexCount) = BuildDisc();
    }

    public void Render(IEnumerable<Entity> entities, World world, Matrix4 view, Matrix4 proj, Vector3 cameraPos)
    {
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);

        mShader.Use();
        GL.BindVertexArray(mVao);

        foreach (var entity in entities)
        {
            if (!entity.IsAlive) 
                continue;

            float shadowSize = entity.ShadowSize;
            if (shadowSize <= 0f) 
                continue;

            float dx = entity.Position.X - cameraPos.X;
            float dz = entity.Position.Z - cameraPos.Z;
            if (dx * dx + dz * dz > FADE_DISTANCE * FADE_DISTANCE) 
                continue;

            float dist  = MathF.Sqrt(dx * dx + dz * dz);
            float alpha = 1f - dist / FADE_DISTANCE;

            if (!TryFindGroundY(entity, world, out float groundY)) 
                continue;

            float heightAbove = entity.Position.Y - groundY;
            alpha *= (1f - Math.Clamp(heightAbove / MAX_DROP, 0f, 1f)) * 0.6f;

            if (alpha <= 0f) 
                continue;

            Matrix4 mvp = Matrix4.CreateScale(shadowSize) * Matrix4.CreateTranslation(entity.Position.X, groundY + Y_BIAS, entity.Position.Z) * view * proj;

            mShader.SetMatrix4("mvp", mvp);
            mShader.SetFloat("uAlpha", alpha);
            GL.DrawArrays(PrimitiveType.TriangleFan, 0, mVertexCount);
        }

        GL.BindVertexArray(0);
        GL.Enable(EnableCap.CullFace);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
    }

    private bool TryFindGroundY(Entity entity, World world, out float groundY)
    {
        int ex = (int)MathF.Floor(entity.Position.X);
        int ez = (int)MathF.Floor(entity.Position.Z);
        int startY = (int)MathF.Floor(entity.Position.Y);

        for (int by = startY; by >= startY - (int)MAX_DROP; by--)
        {
            var block = world.GetBlock(ex, by, ez);
            if (block != BlockType.Air && BlockRegistry.IsSolid(block))
            {
                groundY = by + 1;
                return true;
            }
        }

        groundY = 0f;
        return false;
    }

    // Triangle fan: center + DISC_SEGMENTS edge vertices + repeat first to close the ring.
    private static (int vao, int vbo, int count) BuildDisc()
    {
        int count  = 2 + DISC_SEGMENTS;
        var verts  = new float[count * 2];

        verts[0] = 0f;
        verts[1] = 0f;

        for (int i = 0; i <= DISC_SEGMENTS; i++)
        {
            float angle  = i * MathF.PI * 2f / DISC_SEGMENTS;
            verts[(i + 1) * 2]     = MathF.Cos(angle);
            verts[(i + 1) * 2 + 1] = MathF.Sin(angle);
        }

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);

        return (vao, vbo, count);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(mVao);
        GL.DeleteBuffer(mVbo);
        mShader.Dispose();
    }
}
