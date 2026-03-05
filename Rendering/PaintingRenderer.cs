// Renders all PaintingEntity instances each frame using a dynamic VBO | DA | 2/27/26

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Linq;
using VoxelEngine.GameEntity;
using VoxelEngine.Terrain;

namespace VoxelEngine.Rendering;

public class PaintingRenderer : IDisposable
{
    private int mVao;
    private int mVbo;

    // pos(3) + uv(2) + norm(3) = 8 floats per vertex, matching the entity shader layout
    private const int STRIDE = 8;

    public void Init()
    {
        mVao = GL.GenVertexArray();
        mVbo = GL.GenBuffer();

        GL.BindVertexArray(mVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, STRIDE * sizeof(float), 0);

        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, STRIDE * sizeof(float), 3 * sizeof(float));

        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, STRIDE * sizeof(float), 5 * sizeof(float));

        GL.BindVertexArray(0);
    }

    public void Render(IEnumerable<PaintingEntity> paintings, Texture paintingsTexture, Matrix4 view, Matrix4 proj)
    {
        var paintingList = paintings.ToList();
        if (paintingList.Count == 0)
            return;

        var verts = new List<float>();
        foreach (var p in paintingList)
            BuildFaces(verts, p);

        var shader = Entity._shader;
        if (shader == null)
            return;

        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        var data = verts.ToArray();
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.DynamicDraw);

        GL.Disable(EnableCap.CullFace);
        shader.Use();
        shader.SetFloat("uHitFlash", 0f);
        shader.SetMatrix4("mvp", view * proj);
        paintingsTexture.Use(TextureUnit.Texture0);
        shader.SetInt("tex", 0);
        GL.BindVertexArray(mVao);

        int vertOffset = 0;
        foreach (var p in paintingList)
        {
            int bx = (int)MathF.Floor(p.Position.X);
            int by = (int)MathF.Floor(p.Position.Y);
            int bz = (int)MathF.Floor(p.Position.Z);
            shader.SetFloat("skyLight", World.GetSkyLightGlobal(bx, by, bz) / (float)Terrain.Chunk.MAX_LIGHT);
            shader.SetFloat("blockLight", World.GetBlockLightGlobal(bx, by, bz) / (float)Terrain.Chunk.MAX_LIGHT);

            int vertCount = p.Art.WidthBlocks * p.Art.HeightBlocks * 6;
            GL.DrawArrays(PrimitiveType.Triangles, vertOffset, vertCount);
            vertOffset += vertCount;
        }

        GL.BindVertexArray(0);
        GL.Enable(EnableCap.CullFace);
    }

    private static void BuildFaces(List<float> verts, PaintingEntity p)
    {
        var right = PaintingEntity.WallRightVectors[p.Facing];
        var up = Vector3.UnitY;
        var forward = PaintingEntity.FacingVectors[p.Facing];

        float totalW = p.Art.WidthBlocks;
        float totalH = p.Art.HeightBlocks;
        float atlasSize = 256f;

        for (int tx = 0; tx < p.Art.WidthBlocks; tx++)
        {
            for (int ty = 0; ty < p.Art.HeightBlocks; ty++)
            {
                float offsetRight = tx - totalW / 2f + 0.5f;
                float offsetUp = ty - totalH / 2f + 0.5f;
                var tileCenter = p.Position + right * offsetRight + up * offsetUp;

                var tl = tileCenter - right * 0.5f + up * 0.5f;
                var tr = tileCenter + right * 0.5f + up * 0.5f;
                var bl = tileCenter - right * 0.5f - up * 0.5f;
                var br = tileCenter + right * 0.5f - up * 0.5f;

                float u1 = (p.Art.OffsetX + p.Art.SizeX - tx * 16) / atlasSize;
                float u2 = (p.Art.OffsetX + p.Art.SizeX - (tx + 1) * 16) / atlasSize;

                float vBotEdge = 1f - (p.Art.OffsetY + p.Art.SizeY - ty * 16) / atlasSize;
                float vTopEdge = 1f - (p.Art.OffsetY + p.Art.SizeY - (ty + 1) * 16) / atlasSize;

                EmitVertex(verts, tl, u1, vTopEdge, forward);
                EmitVertex(verts, bl, u1, vBotEdge, forward);
                EmitVertex(verts, br, u2, vBotEdge, forward);

                EmitVertex(verts, br, u2, vBotEdge, forward);
                EmitVertex(verts, tr, u2, vTopEdge, forward);
                EmitVertex(verts, tl, u1, vTopEdge, forward);
            }
        }
    }

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
        GL.DeleteVertexArray(mVao);
        GL.DeleteBuffer(mVbo);
    }
}