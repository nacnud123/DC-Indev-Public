// Blob shadow: a gradient disc projected onto the ground below each entity. | DA | 3/10/26
using Silk.NET.OpenGL;

using VoxelEngine.GameEntity;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Rendering;

/// <summary>
/// Renders a soft gradient disc ("blob shadow") on the ground beneath each live entity. This is a cheap fake-shadow technique (no shadow mapping): a flat, alpha-blended disc mesh is scaled/positioned per entity and faded based on camera distance and height above the ground. Runs in the render order between world geometry and entities.
/// </summary>
public class BlobShadowRenderer : IDisposable
{
    // Number of edge segments used to approximate the shadow disc as a triangle fan; higher = rounder but more verts.
    private const int   DISC_SEGMENTS = 16;
    // Distance (world units) beyond which shadows are fully faded out, to avoid drawing/rendering-cost for far entities.
    private const float FADE_DISTANCE = 64f;
    // Maximum distance below an entity that the renderer will search for ground, and the height above ground at which the shadow is fully faded (e.g. entity jumping/flying away from the surface).
    private const float MAX_DROP = 6f;
    // Small vertical offset added above the ground surface to prevent the shadow quad from z-fighting with the terrain mesh it's drawn on top of.
    private const float Y_BIAS = 0.004f;

    private readonly Shader mShader;
    private readonly uint mVao;
    private readonly uint mVbo;
    // Number of vertices in the disc mesh (center vertex + ring vertices), used for the draw call count.
    private readonly int mVertexCount;

    /// <summary>Loads the shadow disc shader and builds the reusable unit-circle disc mesh.</summary>
    public BlobShadowRenderer()
    {
        mShader = new Shader(File.ReadAllText("Shaders/ShadowDiscVert.glsl"), File.ReadAllText("Shaders/ShadowDiscFrag.glsl"));
        (mVao, mVbo, mVertexCount) = BuildDisc();
    }

    /// <summary>
    /// Draws a blob shadow for every live entity with a non-zero <see cref="Entity.ShadowSize"/>. For each entity: skips if beyond fade distance, raycasts straight down to find the ground block, computes an alpha that fades with distance and height-above-ground, then draws the disc scaled/translated to the entity's footprint. Caller is expected to have depth writes and blending in their normal render-order state; this method manages GL state itself and restores it (blend/cull/depth-mask) before returning.
    /// </summary>
    public void Render(IEnumerable<Entity> entities, World world, Matrix4x4 view, Matrix4x4 proj, Vector3 cameraPos)
    {
        var gl = GlContext.Gl;
        // Shadows are alpha-blended over the terrain and must not occlude/be occluded incorrectly, so depth writes are disabled (still depth-tested via the shared depth buffer) and back-face culling is off since the disc is a flat single-sided quad fan seen from above.
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(false);
        gl.Disable(EnableCap.CullFace);

        mShader.Use();
        gl.BindVertexArray(mVao);

        foreach (var entity in entities)
        {
            if (!entity.IsAlive)
                continue;

            float shadowSize = entity.ShadowSize;
            if (shadowSize <= 0f)
                continue;

            // Distance check uses only X/Z (horizontal) so shadows fade based on how far away the entity appears on the ground plane, ignoring camera height.
            float dx = entity.Position.X - cameraPos.X;
            float dz = entity.Position.Z - cameraPos.Z;
            if (dx * dx + dz * dz > FADE_DISTANCE * FADE_DISTANCE)
                continue;

            float dist  = MathF.Sqrt(dx * dx + dz * dz);
            float alpha = 1f - dist / FADE_DISTANCE;

            if (!TryFindGroundY(entity, world, out float groundY))
                continue;

            // Additional fade as the entity rises above the ground (e.g. jumping), and a flat 0.6 cap so shadows never appear fully opaque/black.
            float heightAbove = entity.Position.Y - groundY;
            alpha *= (1f - Math.Clamp(heightAbove / MAX_DROP, 0f, 1f)) * 0.6f;

            if (alpha <= 0f)
                continue;

            // Model matrix: scale the unit disc to the entity's shadow radius, then translate it onto the entity's X/Z position at the found ground height (plus Y_BIAS). Multiplied by view/proj (row-vector convention, matches System.Numerics * order used throughout this codebase) to get the final clip-space MVP passed to the shader.
            Matrix4x4 mvp = Matrix4x4.CreateScale(shadowSize) * Matrix4x4.CreateTranslation(entity.Position.X, groundY + Y_BIAS, entity.Position.Z) * view * proj;

            mShader.SetMatrix4("mvp", mvp);
            mShader.SetFloat("uAlpha", alpha);
            gl.DrawArrays(PrimitiveType.TriangleFan, 0, (uint)mVertexCount);
        }

        gl.BindVertexArray(0);
        // Restore GL state changed above so later render passes (entities, etc.) aren't affected.
        gl.Enable(EnableCap.CullFace);
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
    }

    /// <summary>
    /// Searches straight down from the entity's feet (up to <see cref="MAX_DROP"/> blocks) for the first solid, non-air block, returning the Y coordinate of its top surface. Used to place the shadow disc on the actual ground rather than at a fixed height.
    /// </summary>
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
                // +1 because block coordinates address the block's lower corner; the top surface of block `by` is at Y = by + 1.
                groundY = by + 1;
                return true;
            }
        }

        groundY = 0f;
        return false;
    }

    /// <summary>
    /// Builds a unit-radius circular disc mesh (2D XY positions, consumed as XZ ground-plane coordinates by the vertex shader) as a triangle fan: a center vertex plus a ring of <see cref="DISC_SEGMENTS"/> perimeter vertices computed via cos/sin around the circle.
    /// </summary>
    private static (uint vao, uint vbo, int count) BuildDisc()
    {
        // +2 = one center vertex + one extra ring vertex to close the fan back to the start angle.
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

        var gl = GlContext.Gl;
        uint vao = gl.GenVertexArray();
        uint vbo = gl.GenBuffer();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts, BufferUsageARB.StaticDraw);
        // Single vec2 attribute (location 0): local-space disc position, scaled/translated in the shader via mvp.
        gl.VertexAttribPointer(0, 2, GLEnum.Float, false, (uint)(2 * sizeof(float)), 0);
        gl.EnableVertexAttribArray(0);
        gl.BindVertexArray(0);

        return (vao, vbo, count);
    }

    /// <summary>Releases the disc mesh's VAO/VBO and the shadow shader program.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVao);
        gl.DeleteBuffer(mVbo);
        mShader.Dispose();
    }
}
