using Silk.NET.OpenGL;

namespace VoxelEngine.Rendering;

/// <summary>
/// A single reusable quad covering the entire clip-space viewport (-1..1 on both axes), used for full-screen post-processing / blit-style passes (e.g. drawing an off-screen framebuffer texture to the screen, screen-space overlays, etc). The vertex shader consuming this mesh is expected to pass the position straight through to gl_Position with no view/projection transform.
/// </summary>
public class FullscreenQuad : IDisposable
{
    private uint _vao, _vbo;

    public FullscreenQuad()
    {
        // Two triangles forming a quad that exactly covers NDC space (-1..1), interleaved as (x, y, u, v) per vertex. UV (0,0) is bottom-left, (1,1) is top-right - matches standard OpenGL texture coordinate convention (V increases upward).
        float[] verts =
        {
            // pos       // uv
            -1f, -1f, 0f, 0f,
            1f, -1f, 1f, 0f,
            1f, 1f, 1f, 1f,
            -1f, -1f, 0f, 0f,
            1f, 1f, 1f, 1f,
            -1f, 1f, 0f, 1f,
        };

        var gl = GlContext.Gl;
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();

        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts, BufferUsageARB.StaticDraw);
        // Interleaved layout: stride 16 bytes = 4 floats per vertex (2 pos + 2 uv). Attribute 0: vec2 position, offset 0.
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, GLEnum.Float, false, 16, 0);
        // Attribute 1: vec2 uv, offset 8 bytes (after the 2 position floats).
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, 16, 8);
        gl.BindVertexArray(0);
    }

    /// <summary>Draws the full-screen quad. Caller must have bound the desired shader/texture beforehand.</summary>
    public void Draw()
    {
        var gl = GlContext.Gl;
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6); // 6 verts = 2 triangles
        gl.BindVertexArray(0);
    }

    /// <summary>Releases the GL vertex array and buffer owned by this quad.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(_vao);
        gl.DeleteBuffer(_vbo);
    }
}
