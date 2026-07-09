// Draws block highlights, has shader hard coded in | DA | 2/5/26
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;


namespace VoxelEngine.Rendering;

/// <summary>
/// Draws the black wireframe outline around the block the player is currently looking at (the "selection box"). Uses a minimal hard-coded shader (position-only, flat black output) since this is just line geometry with no texturing or lighting. Rendered in the block highlight stage of the render order, after transparent world geometry.
/// </summary>
public class BlockHighlight : IDisposable
{
    private readonly uint mVao, mVbo, mShader;

    /// <summary>Builds the unit-cube wireframe mesh (12 edges as line segments) and compiles the inline outline shader.</summary>
    public BlockHighlight()
    {
        // 12 edges of a unit cube, each edge given as a pair of endpoints (drawn with PrimitiveType.Lines, so every consecutive pair of verts is one independent segment): rows are bottom face edges, top face edges, then the 4 vertical edges connecting them.
        float[] verts = {
            0,0,0, 1,0,0, 1,0,0, 1,0,1, 1,0,1, 0,0,1, 0,0,1, 0,0,0,
            0,1,0, 1,1,0, 1,1,0, 1,1,1, 1,1,1, 0,1,1, 0,1,1, 0,1,0,
            0,0,0, 0,1,0, 1,0,0, 1,1,0, 1,0,1, 1,1,1, 0,0,1, 0,1,1
        };

        var gl = GlContext.Gl;
        mVao = gl.GenVertexArray();
        mVbo = gl.GenBuffer();
        gl.BindVertexArray(mVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts, BufferUsageARB.StaticDraw);
        // Single position-only vec3 attribute; stride 0 lets GL infer tightly-packed floats.
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 0u, (nint)0);
        gl.EnableVertexAttribArray(0);

        // Minimal inline shaders: vertex shader just transforms by mvp, fragment shader always outputs opaque black - no texture/lighting needed for a selection outline.
        uint vs = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vs, "#version 330 core\nlayout(location=0)in vec3 p;uniform mat4 mvp;void main(){gl_Position=mvp*vec4(p,1);}");
        gl.CompileShader(vs);

        uint fs = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fs, "#version 330 core\nout vec4 c;void main(){c=vec4(0,0,0,1);}");
        gl.CompileShader(fs);

        mShader = gl.CreateProgram();
        gl.AttachShader(mShader, vs);
        gl.AttachShader(mShader, fs);
        gl.LinkProgram(mShader);
        // Individual shader objects aren't needed after linking into the program.
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
    }

    /// <summary>
    /// Draws the wireframe box around block <paramref name="pos"/>, scaled/offset to the block's actual collision bounds (<paramref name="boundsMin"/>/<paramref name="boundsMax"/>, which may be smaller than a full unit cube for slabs, stairs, etc.) and inflated slightly so the outline sits just outside the block's faces instead of z-fighting with them.
    /// </summary>
    public void Render(Vector3i pos, Matrix4x4 view, Matrix4x4 proj, Vector3 boundsMin, Vector3 boundsMax)
    {
        // Scale the unit-cube wireframe to the block's actual bounds, inflated by 1% so the lines render just outside the block's surfaces (avoids z-fighting with the block mesh).
        Vector3 size = (boundsMax - boundsMin) * 1.01f;
        // Shrink the origin offset by half the inflation (0.5%) on each side so the box grows symmetrically outward from the block's bounds rather than only in the +axis directions.
        Vector3 offset = boundsMin - (boundsMax - boundsMin) * 0.005f;
        Matrix4x4 model = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(pos.X + offset.X, pos.Y + offset.Y, pos.Z + offset.Z);
        Matrix4x4 mvp = model * view * proj;

        var gl = GlContext.Gl;
        gl.UseProgram(mShader);
        int loc = gl.GetUniformLocation(mShader, "mvp");
        // Reinterpret the Matrix4x4's floats as a span to upload directly (equivalent to the `fixed(float* p = &m.M11)` pattern the Shader helper class uses elsewhere).
        gl.UniformMatrix4(loc, 1, false,
            MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref mvp, 1)));
        gl.LineWidth(2f);
        gl.BindVertexArray(mVao);
        // 24 = 12 edges * 2 verts/edge (Lines primitive, not LineStrip/LineLoop).
        gl.DrawArrays(PrimitiveType.Lines, 0, 24);
        // Reset line width back to the GL default so it doesn't leak into other line-drawing code.
        gl.LineWidth(1f);
    }

    /// <summary>Releases the wireframe mesh's VAO/VBO and the outline shader program.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVao);
        gl.DeleteBuffer(mVbo);
        gl.DeleteProgram(mShader);
    }
}
