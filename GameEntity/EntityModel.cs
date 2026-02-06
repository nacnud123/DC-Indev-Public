// Main file for entity model, it holds reference to GL stuff like Vao, Vbo, Texture, and VertexCount | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

public class EntityModel : IDisposable
{
    private static readonly Dictionary<string, EntityModel> Cache = new();

    public int Vao { get; }
    public int Vbo { get; }
    public int VertexCount { get; }
    public Texture Texture { get; }

    private EntityModel(int vao, int vbo, int vertexCount, Texture texture)
    {
        Vao = vao;
        Vbo = vbo;
        VertexCount = vertexCount;
        Texture = texture;
    }

    public static EntityModel Load(string modelPath, string texturePath)
    {
        string key = $"{modelPath}|{texturePath}";
        
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var loader = new ObjLoader();
        loader.Load(modelPath);

        var texture = Texture.LoadFromFile(texturePath);

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, loader.Vertices.Length * sizeof(float), loader.Vertices, BufferUsageHint.StaticDraw);

        int stride = 8 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        var model = new EntityModel(vao, vbo, loader.VertexCount, texture);
        Cache[key] = model;
        return model;
    }

    public static void DisposeAll()
    {
        foreach (var model in Cache.Values)
        {
            model.Dispose();
        }
        Cache.Clear();
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(Vao);
        GL.DeleteBuffer(Vbo);
        Texture.Dispose();
    }
}
