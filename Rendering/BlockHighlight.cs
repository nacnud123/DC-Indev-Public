// Draws block highlights, has shader hard coded in | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace VoxelEngine.Rendering;

public class BlockHighlight : IDisposable
{
    private readonly int mVao, mVbo, mShader;

    public BlockHighlight()
    {
        float[] verts = {
            0,0,0, 1,0,0, 1,0,0, 1,0,1, 1,0,1, 0,0,1, 0,0,1, 0,0,0,
            0,1,0, 1,1,0, 1,1,0, 1,1,1, 1,1,1, 0,1,1, 0,1,1, 0,1,0,
            0,0,0, 0,1,0, 1,0,0, 1,1,0, 1,0,1, 1,1,1, 0,0,1, 0,1,1
        };

        mVao = GL.GenVertexArray();
        mVbo = GL.GenBuffer();
        GL.BindVertexArray(mVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
        GL.EnableVertexAttribArray(0);

        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, "#version 330 core\nlayout(location=0)in vec3 p;uniform mat4 mvp;void main(){gl_Position=mvp*vec4(p,1);}");
        GL.CompileShader(vs);

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, "#version 330 core\nout vec4 c;void main(){c=vec4(0,0,0,1);}");
        GL.CompileShader(fs);

        mShader = GL.CreateProgram();
        GL.AttachShader(mShader, vs);
        GL.AttachShader(mShader, fs);
        GL.LinkProgram(mShader);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
    }

    public void Render(Vector3i pos, Matrix4 view, Matrix4 proj, Vector3 boundsMin, Vector3 boundsMax)
    {
        Vector3 size = (boundsMax - boundsMin) * 1.01f;
        Vector3 offset = boundsMin - (boundsMax - boundsMin) * 0.005f;
        Matrix4 model = Matrix4.CreateScale(size) * Matrix4.CreateTranslation(pos.X + offset.X, pos.Y + offset.Y, pos.Z + offset.Z);
        Matrix4 mvp = model * view * proj;

        GL.UseProgram(mShader);
        GL.UniformMatrix4(GL.GetUniformLocation(mShader, "mvp"), false, ref mvp);
        GL.LineWidth(2f);
        GL.BindVertexArray(mVao);
        GL.DrawArrays(PrimitiveType.Lines, 0, 24);
        GL.LineWidth(1f);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(mVao);
        GL.DeleteBuffer(mVbo);
        GL.DeleteProgram(mShader);
    }
}
