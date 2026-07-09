// Main shader class, does stuff to load and unload shader. Also, has functions for setting in the shader | DA | 2/5/26
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;


namespace VoxelEngine.Rendering;

/// <summary>
/// Wraps a linked GLSL shader program (one vertex + one fragment stage). Compiles both stages, links them into a program, caches every active uniform's location up front, and exposes typed setters so callers never have to look up uniform locations themselves. One instance is created per distinct shader program used by the renderer (terrain, entities, sky, UI, etc.).
/// </summary>
public class Shader : IDisposable
{
    public uint Handle { get; }

    // Uniform name -> GL location, resolved once at link time so per-frame SetX calls are just a dictionary lookup instead of a GetUniformLocation round-trip to the driver every call.
    private readonly Dictionary<string, int> mUniforms = new();

    /// <summary>
    /// Compiles, links, and validates a shader program from GLSL source strings. Throws with the driver's info log if compilation or linking fails, which surfaces GLSL syntax errors immediately at startup rather than as a silent black-screen/no-draw failure later.
    /// </summary>
    public Shader(string vertexSrc, string fragmentSrc)
    {
        var gl = GlContext.Gl;

        uint vs = CompileShader(gl, ShaderType.VertexShader, vertexSrc);
        uint fs = CompileShader(gl, ShaderType.FragmentShader, fragmentSrc);

        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, vs);
        gl.AttachShader(Handle, fs);
        gl.LinkProgram(Handle);
        gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int success);

        if (success == 0)
            throw new Exception(gl.GetProgramInfoLog(Handle));

        // Once linked, the individual shader stage objects are no longer needed - the program keeps its own copy of the compiled code. Detach then delete to let the driver free them immediately instead of leaving them alive (attached) for the lifetime of the program.
        gl.DetachShader(Handle, vs);
        gl.DetachShader(Handle, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);

        // Enumerate every uniform the linker actually kept (unused uniforms get optimized out and won't appear here) and cache its location so SetInt/SetFloat/etc. never need GetUniformLocation again.
        gl.GetProgram(Handle, ProgramPropertyARB.ActiveUniforms, out int count);

        for (uint i = 0; i < (uint)count; i++)
        {
            string name = gl.GetActiveUniform(Handle, i, out _, out _);
            mUniforms[name] = gl.GetUniformLocation(Handle, name);
        }
    }

    // Compiles a single shader stage (vertex or fragment) and throws the driver's info log on failure.
    private static uint CompileShader(GL gl, ShaderType type, string src)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, src);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);

        if (success == 0)
            throw new Exception(gl.GetShaderInfoLog(shader));

        return shader;
    }

    /// <summary>Binds this program as the active GL shader for subsequent draw calls.</summary>
    public void Use() => GlContext.Gl.UseProgram(Handle);

    // All SetX methods silently no-op if `name` isn't a known active uniform (e.g. the GLSL compiler optimized it out because it's unused in that particular shader variant), so callers can share one code path across shaders that don't all declare the same uniform set without needing to branch.
    public void SetInt(string name, int v)
    {
        if (mUniforms.TryGetValue(name, out int loc))
            GlContext.Gl.Uniform1(loc, v);
    }

    public void SetFloat(string name, float v)
    {
        if (mUniforms.TryGetValue(name, out int loc))
            GlContext.Gl.Uniform1(loc, v);
    }

    public void SetVector2(string name, Vector2 v)
    {
        if (mUniforms.TryGetValue(name, out int loc))
            GlContext.Gl.Uniform2(loc, v.X, v.Y);
    }

    public void SetVector3(string name, Vector3 v)
    {
        if (mUniforms.TryGetValue(name, out int loc))
            GlContext.Gl.Uniform3(loc, v.X, v.Y, v.Z);
    }

    /// <summary>
    /// Uploads a 4x4 matrix uniform (view/projection/model/MVP, etc.). <see cref="System.Numerics.Matrix4x4"/> stores its 16 components (M11..M44) as sequential fields with no padding, so its in-memory layout is bit-for-bit identical to a flat float[16]. This lets us reinterpret the matrix as a span of floats via <see cref="MemoryMarshal"/> instead of copying it into an intermediate array - no unsafe pointer arithmetic needed, unlike some other GL uniform helpers in this codebase that use `fixed` for the same purpose. `false` for the transpose argument means the data is uploaded as-is; System.Numerics.Matrix4x4 is row-vector/row-major on the CPU side, and GLSL's `mat4` constructor/multiplication conventions in this project's shaders are written to match that layout directly.
    /// </summary>
    public void SetMatrix4(string name, Matrix4x4 m)
    {
        if (mUniforms.TryGetValue(name, out int loc))
            GlContext.Gl.UniformMatrix4(loc, 1, false,
                MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref m, 1)));
    }

    /// <summary>Deletes the underlying GL program object. Does not null out Handle - do not use after disposing.</summary>
    public void Dispose() => GlContext.Gl.DeleteProgram(Handle);
}
