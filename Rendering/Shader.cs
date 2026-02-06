// Main shader class, does stuff to load and unload shader. Also, has functions for setting in the shader | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace VoxelEngine.Rendering;

public class Shader : IDisposable
{
    public int Handle { get; }
    private readonly Dictionary<string, int> mUniforms = new();

    public Shader(string vertexSrc, string fragmentSrc)
    {
        int vs = CompileShader(ShaderType.VertexShader, vertexSrc);
        int fs = CompileShader(ShaderType.FragmentShader, fragmentSrc);

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vs);
        GL.AttachShader(Handle, fs);
        GL.LinkProgram(Handle);
        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int success);
        
        if (success == 0) 
            throw new Exception(GL.GetProgramInfoLog(Handle));

        GL.DetachShader(Handle, vs);
        GL.DetachShader(Handle, fs);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);

        GL.GetProgram(Handle, GetProgramParameterName.ActiveUniforms, out int count);
        
        for (int i = 0; i < count; i++)
        {
            string name = GL.GetActiveUniform(Handle, i, out _, out _);
            mUniforms[name] = GL.GetUniformLocation(Handle, name);
        }
    }

    private static int CompileShader(ShaderType type, string src)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, src);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        
        if (success == 0) 
            throw new Exception(GL.GetShaderInfoLog(shader));
        
        return shader;
    }

    public void Use() => GL.UseProgram(Handle);

    public void SetInt(string name, int v)
    {
        if (mUniforms.TryGetValue(name, out int loc)) 
            GL.Uniform1(loc, v);
    }

    public void SetFloat(string name, float v)
    {
        if (mUniforms.TryGetValue(name, out int loc)) 
            GL.Uniform1(loc, v);
    }

    public void SetVector3(string name, Vector3 v)
    {
        if (mUniforms.TryGetValue(name, out int loc)) 
            GL.Uniform3(loc, v);
    }

    public void SetMatrix4(string name, Matrix4 m)
    {
        if (mUniforms.TryGetValue(name, out int loc)) 
            GL.UniformMatrix4(loc, false, ref m);
    }

    public void Dispose() => GL.DeleteProgram(Handle);
}
