// Main class that draws the block breaking animation. It makes a cube that goes over the block that is being broken, the cube is slightly bigger than the block to make sure Z-fighting does not happen | DA | 2/14/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace VoxelEngine.Rendering;

public class BlockBreakOverlay : IDisposable
{
    private readonly int mVao, mVbo, mShader;

    private const string VertexShaderSource = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
uniform mat4 mvp;
out vec2 vUv;
void main() {
    gl_Position = mvp * vec4(aPos, 1.0);
    vUv = aUv;
}";

    private const string FragmentShaderSource = @"#version 330 core
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D breakTexture;
void main() {
    float a = texture(breakTexture, vUv).a;
    fragColor = vec4(0.0, 0.0, 0.0, a * 0.6);
}";

    public BlockBreakOverlay()
    {
        mVao = GL.GenVertexArray();
        mVbo = GL.GenBuffer();
        GL.BindVertexArray(mVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        // 36 verts * 5 floats (pos + uv) - allocate max, update each frame
        GL.BufferData(BufferTarget.ArrayBuffer, 36 * 5 * sizeof(float), nint.Zero, BufferUsageHint.DynamicDraw);
        // position
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        // uv
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, VertexShaderSource);
        GL.CompileShader(vs);

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, FragmentShaderSource);
        GL.CompileShader(fs);

        mShader = GL.CreateProgram();
        GL.AttachShader(mShader, vs);
        GL.AttachShader(mShader, fs);
        GL.LinkProgram(mShader);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
    }

    public void Render(Vector3i pos, int stage, Matrix4 view, Matrix4 proj, Texture breakTexture)
    {
        if (stage < 0 || stage > 6)
            return;

        float u0 = stage / 7f;
        float u1 = (stage + 1) / 7f;

        const float e = 0.001f; // slight expansion to avoid z-fighting
        float x0 = pos.X - e, y0 = pos.Y - e, z0 = pos.Z - e;
        float x1 = pos.X + 1 + e, y1 = pos.Y + 1 + e, z1 = pos.Z + 1 + e;

        float[] verts = {
            // Front face (z+)
            x0,y0,z1, u0,1, x1,y0,z1, u1,1, x1,y1,z1, u1,0,
            x0,y0,z1, u0,1, x1,y1,z1, u1,0, x0,y1,z1, u0,0,
            // Back face (z-)
            x1,y0,z0, u0,1, x0,y0,z0, u1,1, x0,y1,z0, u1,0,
            x1,y0,z0, u0,1, x0,y1,z0, u1,0, x1,y1,z0, u0,0,
            // Right face (x+)
            x1,y0,z1, u0,1, x1,y0,z0, u1,1, x1,y1,z0, u1,0,
            x1,y0,z1, u0,1, x1,y1,z0, u1,0, x1,y1,z1, u0,0,
            // Left face (x-)
            x0,y0,z0, u0,1, x0,y0,z1, u1,1, x0,y1,z1, u1,0,
            x0,y0,z0, u0,1, x0,y1,z1, u1,0, x0,y1,z0, u0,0,
            // Top face (y+)
            x0,y1,z1, u0,1, x1,y1,z1, u1,1, x1,y1,z0, u1,0,
            x0,y1,z1, u0,1, x1,y1,z0, u1,0, x0,y1,z0, u0,0,
            // Bottom face (y-)
            x0,y0,z0, u0,1, x1,y0,z0, u1,1, x1,y0,z1, u1,0,
            x0,y0,z0, u0,1, x1,y0,z1, u1,0, x0,y0,z1, u0,0,
        };

        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, nint.Zero, verts.Length * sizeof(float), verts);

        Matrix4 mvp = Matrix4.Identity * view * proj;
        GL.UseProgram(mShader);
        GL.UniformMatrix4(GL.GetUniformLocation(mShader, "mvp"), false, ref mvp);

        breakTexture.Use(TextureUnit.Texture0);
        GL.Uniform1(GL.GetUniformLocation(mShader, "breakTexture"), 0);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(-1f, -1f);

        GL.BindVertexArray(mVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36);

        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.Enable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(mVao);
        GL.DeleteBuffer(mVbo);
        GL.DeleteProgram(mShader);
    }
}
