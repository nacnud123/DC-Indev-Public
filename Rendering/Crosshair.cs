// Draws crosshair, can probably be made using ImGui now | DA | 2/5/26
using Silk.NET.OpenGL;

namespace VoxelEngine.Rendering;

/// <summary>
/// Draws the simple two-line crosshair in the center of the screen. Deliberately minimal: builds its own tiny inline GLSL shader (rather than loading .glsl files like other renderers) since the crosshair is just two flat-colored line segments in clip space with no transform needed. Rendered every frame with depth testing disabled so it always sits on top.
/// </summary>
public class Crosshair : IDisposable
{
    private readonly uint mVao, mVbo, mShader;

    public Crosshair()
    {
        // Half-length of each crosshair line, in normalized device coordinates (clip space).
        float s = 0.02f;
        // Two line segments (4 verts) already positioned in clip space (-1..1), centered at origin: horizontal line (-s,0)->(s,0), vertical line (0,-1.5s)->(0,1.5s). The vertical line is scaled 1.5x so the crosshair reads as visually balanced (fonts/aspect make a plain square cross look horizontally squashed).
        float[] verts = { -s, 0, s, 0, 0, -s * 1.5f, 0, s * 1.5f };

        var gl = GlContext.Gl;
        mVao = gl.GenVertexArray();
        mVbo = gl.GenBuffer();
        gl.BindVertexArray(mVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts, BufferUsageARB.StaticDraw);
        // Layout 0: vec2 position, tightly packed, no other attributes.
        gl.VertexAttribPointer(0, 2, GLEnum.Float, false, 0, 0);
        gl.EnableVertexAttribArray(0);

        // Trivial vertex shader: input position is already in clip space (NDC), so it's passed straight through to gl_Position with no view/projection transform.
        uint vs = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vs, "#version 330 core\nlayout(location=0)in vec2 p;void main(){gl_Position=vec4(p,0,1);}");
        gl.CompileShader(vs);

        // Trivial fragment shader: always outputs opaque white - the crosshair has no texture or color uniform.
        uint fs = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fs, "#version 330 core\nout vec4 c;void main(){c=vec4(1);}");
        gl.CompileShader(fs);

        mShader = gl.CreateProgram();
        gl.AttachShader(mShader, vs);
        gl.AttachShader(mShader, fs);
        gl.LinkProgram(mShader);
        // Shader objects can be deleted once linked into the program; the program retains the compiled code.
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
    }

    /// <summary>
    /// Draws the crosshair as two GL_LINES segments. Caller (GameRenderer.RenderHud) is expected to disable depth testing beforehand so the crosshair isn't occluded by world geometry.
    /// </summary>
    public void Render()
    {
        var gl = GlContext.Gl;
        gl.UseProgram(mShader);
        gl.BindVertexArray(mVao);
        gl.DrawArrays(PrimitiveType.Lines, 0, 4); // 4 verts = 2 independent line segments
    }

    /// <summary>Releases the GL buffer, vertex array, and shader program owned by this crosshair.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVao);
        gl.DeleteBuffer(mVbo);
        gl.DeleteProgram(mShader);
    }
}
