// Renders all PaintingEntity instances each frame using a dynamic VBO | DA | 2/27/26

using Silk.NET.OpenGL;

using System.Linq;
using VoxelEngine.GameEntity;
using VoxelEngine.Terrain;

namespace VoxelEngine.Rendering;

/// <summary>
/// Draws every placed <see cref="PaintingEntity"/> in the world each frame. Painting geometry is rebuilt into a single dynamic VBO every call to <see cref="Render"/> (paintings are rare and the vertex count is small, so re-uploading per frame is simpler than tracking per-painting dirty state). Reuses the shared entity shader (<see cref="Entity._shader"/>) rather than owning its own shader, since painting quads are lit/shaded the same way as mob/player models.
/// </summary>
public class PaintingRenderer : IDisposable
{
    private uint mVao;
    private uint mVbo;

    // pos(3) + uv(2) + norm(3) = 8 floats per vertex, matching the entity shader layout
    private const int STRIDE = 8;

    /// <summary>Creates the VAO/VBO and wires up the vertex attribute layout. Call once at startup.</summary>
    public void Init()
    {
        var gl = GlContext.Gl;
        mVao = gl.GenVertexArray();
        mVbo = gl.GenBuffer();

        gl.BindVertexArray(mVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        // Allocate with a null pointer and zero size up front - the buffer is fully re-uploaded via BufferData in Render() every frame, so this call just reserves the GL object. `unsafe` is required here because Silk.NET's raw BufferData overload takes a native void* pointer.
        unsafe { gl.BufferData(BufferTargetARB.ArrayBuffer, 0, (void*)null, BufferUsageARB.DynamicDraw); }

        // Attribute 0: vertex position (vec3), attribute 1: UV (vec2), attribute 2: normal (vec3). Offsets below are in bytes into each STRIDE-float vertex record, matching BuildFaces/EmitVertex.
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(STRIDE * sizeof(float)), 0);

        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(STRIDE * sizeof(float)), (nint)(3 * sizeof(float)));

        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, (uint)(STRIDE * sizeof(float)), (nint)(5 * sizeof(float)));

        gl.BindVertexArray(0);
    }

    /// <summary>
    /// Rebuilds the painting quad geometry from scratch and draws every painting in one pass. Called once per frame from the main render loop after opaque/transparent world geometry.
    /// </summary>
    public void Render(IEnumerable<PaintingEntity> paintings, Texture paintingsTexture, Matrix4x4 view, Matrix4x4 proj)
    {
        var paintingList = paintings.ToList();
        if (paintingList.Count == 0)
            return;

        // Rebuild the full vertex list for all paintings up front (CPU side), then upload once.
        var verts = new List<float>();
        foreach (var p in paintingList)
            BuildFaces(verts, p);

        var shader = Entity._shader;
        if (shader == null)
            return;

        var gl = GlContext.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        var data = verts.ToArray();
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data, BufferUsageARB.DynamicDraw);

        // Painting quads are single-sided planes anchored flush against a wall; culling is disabled so the visible face still renders regardless of winding order/facing, then restored below.
        gl.Disable(EnableCap.CullFace);
        shader.Use();
        shader.SetFloat("uHitFlash", 0f);
        shader.SetMatrix4("mvp", view * proj);
        paintingsTexture.Use(TextureUnit.Texture0);
        shader.SetInt("tex", 0);
        gl.BindVertexArray(mVao);

        // One draw call per painting (not one big draw call) because each painting has its own per-block sky/block light values sampled from its world position and pushed as uniforms.
        int vertOffset = 0;
        foreach (var p in paintingList)
        {
            int bx = (int)MathF.Floor(p.Position.X);
            int by = (int)MathF.Floor(p.Position.Y);
            int bz = (int)MathF.Floor(p.Position.Z);
            shader.SetFloat("skyLight", World.GetSkyLightGlobal(bx, by, bz) / (float)Terrain.Chunk.MAX_LIGHT);
            shader.SetFloat("blockLight", World.GetBlockLightGlobal(bx, by, bz) / (float)Terrain.Chunk.MAX_LIGHT);

            // Each 1x1 tile of the painting is 2 triangles = 6 vertices (see BuildFaces).
            int vertCount = p.Art.WidthBlocks * p.Art.HeightBlocks * 6;
            gl.DrawArrays(PrimitiveType.Triangles, vertOffset, (uint)vertCount);
            vertOffset += vertCount;
        }

        gl.BindVertexArray(0);
        gl.Enable(EnableCap.CullFace);
    }

    // Builds a grid of 1x1-block quads (2 triangles each) covering a single painting, one tile at a time, so multi-block-sized paintings (e.g. a 2x2 painting) get individually addressable UV tiles out of the paintings atlas instead of one stretched quad.
    private static void BuildFaces(List<float> verts, PaintingEntity p)
    {
        // right/up/forward form the painting's local basis in world space: `right` runs along the wall (perpendicular to the wall normal), `up` is always world-up, and `forward` is the wall normal used to light/orient the quad (paintings don't tilt, so up is never re-derived via cross product).
        var right = PaintingEntity.WallRightVectors[p.Facing];
        var up = Vector3.UnitY;
        var forward = PaintingEntity.FacingVectors[p.Facing];

        float totalW = p.Art.WidthBlocks;
        float totalH = p.Art.HeightBlocks;
        float atlasSize = 256f; // paintings atlas texture is a fixed 256x256 image

        for (int tx = 0; tx < p.Art.WidthBlocks; tx++)
        {
            for (int ty = 0; ty < p.Art.HeightBlocks; ty++)
            {
                // Center each 1x1 tile relative to the painting's own center (p.Position), so the whole painting is symmetric around its anchor point regardless of width/height in blocks.
                float offsetRight = tx - totalW / 2f + 0.5f;
                float offsetUp = ty - totalH / 2f + 0.5f;
                var tileCenter = p.Position + right * offsetRight + up * offsetUp;

                // Four corners of this tile's quad, built from the local right/up basis rather than world axes so painting orientation follows the wall it's mounted on.
                var tl = tileCenter - right * 0.5f + up * 0.5f;
                var tr = tileCenter + right * 0.5f + up * 0.5f;
                var bl = tileCenter - right * 0.5f - up * 0.5f;
                var br = tileCenter + right * 0.5f - up * 0.5f;

                // Each painting "art" entry (p.Art) defines a sub-rectangle in the shared atlas via OffsetX/OffsetY/SizeX/SizeY (in pixels). U runs right-to-left here (note the subtraction order) because `right` and atlas-column order are mirrored for wall-mounted paintings.
                float u1 = (p.Art.OffsetX + p.Art.SizeX - tx * 16) / atlasSize;
                float u2 = (p.Art.OffsetX + p.Art.SizeX - (tx + 1) * 16) / atlasSize;

                // V is flipped (1f - ...) to convert from the atlas's top-left pixel origin to OpenGL's bottom-left-origin UV space.
                float vBotEdge = 1f - (p.Art.OffsetY + p.Art.SizeY - ty * 16) / atlasSize;
                float vTopEdge = 1f - (p.Art.OffsetY + p.Art.SizeY - (ty + 1) * 16) / atlasSize;

                // Two triangles forming the tile quad (tl,bl,br) and (br,tr,tl).
                EmitVertex(verts, tl, u1, vTopEdge, forward);
                EmitVertex(verts, bl, u1, vBotEdge, forward);
                EmitVertex(verts, br, u2, vBotEdge, forward);

                EmitVertex(verts, br, u2, vBotEdge, forward);
                EmitVertex(verts, tr, u2, vTopEdge, forward);
                EmitVertex(verts, tl, u1, vTopEdge, forward);
            }
        }
    }

    // Appends one vertex record (pos.xyz, uv.xy, normal.xyz = STRIDE floats) to the CPU-side vertex list.
    private static void EmitVertex(List<float> verts, Vector3 pos, float u, float v, Vector3 norm)
    {
        verts.Add(pos.X);
        verts.Add(pos.Y);
        verts.Add(pos.Z);
        verts.Add(u);
        verts.Add(v);
        verts.Add(norm.X);
        verts.Add(norm.Y);
        verts.Add(norm.Z);
    }

    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVao);
        gl.DeleteBuffer(mVbo);
    }
}
