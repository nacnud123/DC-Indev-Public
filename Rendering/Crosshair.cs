// Draws crosshair, can probably be made using ImGui now | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;

namespace VoxelEngine.Rendering;

public class Crosshair : IDisposable
{
    private readonly int mVao, mVbo, mShader;

    public Crosshair()
    {
        float s = 0.02f;
        float[] verts = { -s, 0, s, 0, 0, -s * 1.5f, 0, s * 1.5f };

        mVao = GL.GenVertexArray();
        mVbo = GL.GenBuffer();
        GL.BindVertexArray(mVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 0, 0);
        GL.EnableVertexAttribArray(0);

        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, "#version 330 core\nlayout(location=0)in vec2 p;void main(){gl_Position=vec4(p,0,1);}");
        GL.CompileShader(vs);

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, "#version 330 core\nout vec4 c;void main(){c=vec4(1);}");
        GL.CompileShader(fs);

        mShader = GL.CreateProgram();
        GL.AttachShader(mShader, vs);
        GL.AttachShader(mShader, fs);
        GL.LinkProgram(mShader);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
    }

    public void Render()
    {
        GL.UseProgram(mShader);
        GL.BindVertexArray(mVao);
        GL.DrawArrays(PrimitiveType.Lines, 0, 4);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(mVao);
        GL.DeleteBuffer(mVbo);
        GL.DeleteProgram(mShader);
    }
}
