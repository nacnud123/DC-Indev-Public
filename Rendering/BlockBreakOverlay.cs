// Main class that draws the block breaking animation. It makes a cube that goes over the block that is being broken, the cube is slightly bigger than the block to make sure Z-fighting does not happen | DA | 2/14/26
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;


namespace VoxelEngine.Rendering;

/// <summary>
/// Draws the "crack" overlay cube shown over a block while the player is breaking it. The cube is rendered slightly larger than a unit block (see <c>e</c> in <see cref="Render"/>) so its faces sit just outside the target block's faces, avoiding z-fighting with the terrain mesh. The shader source is inlined here (rather than loaded from Shaders/) since this is a tiny, self-contained effect.
/// </summary>
public class BlockBreakOverlay : IDisposable
{
    private readonly uint mVao, mVbo, mShader;

    // Vertex shader: transforms cube positions by mvp and passes through the break-stage UV.
    private const string VertexShaderSource = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
uniform mat4 mvp;
out vec2 vUv;
void main() {
    gl_Position = mvp * vec4(aPos, 1.0);
    vUv = aUv;
}";

    // Fragment shader: samples the alpha channel of the break-stage texture (crack pattern) and uses it as the overlay's opacity, tinted black at 60% max alpha so cracks darken the block without ever going fully opaque.
    private const string FragmentShaderSource = @"#version 330 core
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D breakTexture;
void main() {
    float a = texture(breakTexture, vUv).a;
    fragColor = vec4(0.0, 0.0, 0.0, a * 0.6);
}";

    /// <summary>Allocates the dynamic cube VBO and compiles/links the inline crack-overlay shader.</summary>
    public BlockBreakOverlay()
    {
        var gl = GlContext.Gl;

        mVao = gl.GenVertexArray();
        mVbo = gl.GenBuffer();
        gl.BindVertexArray(mVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        unsafe
        {
            // 36 verts * 5 floats (pos + uv) - allocate max, update each frame. DynamicDraw + null initial data because the cube's vertex positions are rewritten every Render() call (they depend on which block is targeted), so this only reserves GPU-side storage; BufferSubData uploads the actual per-frame contents below.
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(36 * 5 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
        }
        // Interleaved layout: position (vec3) then UV (vec2), stride 5 floats.
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(5 * sizeof(float)), (nint)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(5 * sizeof(float)), (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);

        uint vs = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vs, VertexShaderSource);
        gl.CompileShader(vs);

        uint fs = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fs, FragmentShaderSource);
        gl.CompileShader(fs);

        mShader = gl.CreateProgram();
        gl.AttachShader(mShader, vs);
        gl.AttachShader(mShader, fs);
        gl.LinkProgram(mShader);
        // Shader objects can be freed once linked into the program; the program retains the compiled code.
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
    }

    /// <summary>
    /// Rebuilds the cube mesh around <paramref name="pos"/> for the given break <paramref name="stage"/> (0-6, mapping to 7 crack-texture frames stored side by side in <paramref name="breakTexture"/>), uploads it, and draws it with additive-style alpha blending on top of the world.
    /// </summary>
    public void Render(Vector3i pos, int stage, Matrix4x4 view, Matrix4x4 proj, Texture breakTexture)
    {
        if (stage < 0 || stage > 6)
            return;

        // The break texture atlas has 7 horizontal frames (stages 0-6); pick out this stage's UV slice.
        float u0 = stage / 7f;
        float u1 = (stage + 1) / 7f;

        const float e = 0.001f; // slight expansion to avoid z-fighting
        // Expand the cube bounds by e on every axis so its faces are just outside the target block's faces (which span [pos, pos+1]), preventing the overlay from z-fighting with the block's own rendered faces.
        float x0 = pos.X - e, y0 = pos.Y - e, z0 = pos.Z - e;
        float x1 = pos.X + 1 + e, y1 = pos.Y + 1 + e, z1 = pos.Z + 1 + e;

        // 6 faces * 2 triangles * 3 verts, each vert = (x,y,z,u,v). Every face reuses the same u0/u1 UV strip (the crack pattern is applied uniformly to every visible face) with v flipped between 0/1 to orient the quad correctly per face winding.
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

        var gl = GlContext.Gl;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        // Overwrite the whole pre-allocated buffer with this frame's cube geometry.
        gl.BufferSubData<float>(BufferTargetARB.ArrayBuffer, 0, verts);

        // Model matrix is identity (verts are already in world space above), so mvp is just view*proj.
        Matrix4x4 mvp = Matrix4x4.Identity * view * proj;
        gl.UseProgram(mShader);
        int mvpLoc = gl.GetUniformLocation(mShader, "mvp");
        // MemoryMarshal reinterprets the Matrix4x4's 16 floats as a contiguous span without copying, which is what UniformMatrix4 needs; this codebase's Shader class normally does the equivalent via `fixed(float* p = &m.M11)`, but this class manages GL calls manually.
        gl.UniformMatrix4(mvpLoc, 1, false,
            MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref mvp, 1)));

        breakTexture.Use(TextureUnit.Texture0);
        gl.Uniform1(gl.GetUniformLocation(mShader, "breakTexture"), 0);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        // Overlay cube is drawn from outside/inside alike depending on camera position relative to the block, so culling is disabled to guarantee all faces render.
        gl.Disable(EnableCap.CullFace);
        // Polygon offset pushes the overlay's depth values away from the camera slightly, which (combined with the `e` expansion above) further guards against z-fighting with terrain.
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(-1f, -1f);

        gl.BindVertexArray(mVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 36);

        // Restore GL state so later draw calls (which assume culling/blending defaults) aren't affected.
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.Enable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
    }

    /// <summary>Releases the VAO/VBO and the compiled shader program.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVao);
        gl.DeleteBuffer(mVbo);
        gl.DeleteProgram(mShader);
    }
}
