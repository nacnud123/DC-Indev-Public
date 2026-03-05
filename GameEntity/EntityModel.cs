// Main file for entity model, it holds reference to GL stuff like Vao, Vbo, Texture, and VertexCount | DA | 2/5/26

using OpenTK.Graphics.OpenGL4;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

public class EntityModel : IDisposable
{
    private static readonly Dictionary<string, EntityModel> Cache = new();
    private static readonly Dictionary<string, EntityModel[]> MtlCache = new();

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

        var model = Upload(loader.Vertices, loader.VertexCount, Texture.LoadFromFile(texturePath));
        Cache[key] = model;
        return model;
    }

    public static EntityModel[] LoadWithMtl(string modelPath, string mtlPath)
    {
        string key = $"{modelPath}|{mtlPath}";

        if (MtlCache.TryGetValue(key, out var cached))
            return cached;

        var loader = new ObjLoader();
        loader.Load(modelPath);

        var matTextures = MtlLoader.Load(mtlPath);
        var results = new List<EntityModel>();

        foreach (var (mat, verts) in loader.MaterialGroups)
        {
            if (verts.Length == 0) 
                continue;
            
            if (!matTextures.TryGetValue(mat, out var texPath)) 
                continue;

            results.Add(Upload(verts, verts.Length / ObjLoader.FLOATS_PER_VERTEX, Texture.LoadFromFile(texPath)));
        }

        var parts = results.ToArray();
        MtlCache[key] = parts;
        return parts;
    }

    private static EntityModel Upload(float[] vertices, int vertexCount, Texture texture)
    {
        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        int stride = ObjLoader.FLOATS_PER_VERTEX * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        GL.BindVertexArray(0);
        return new EntityModel(vao, vbo, vertexCount, texture);
    }

    public static void DisposeAll()
    {
        foreach (var model in Cache.Values)
            model.Dispose();
        Cache.Clear();

        foreach (var parts in MtlCache.Values)
        foreach (var model in parts)
            model.Dispose();
        MtlCache.Clear();
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(Vao);
        GL.DeleteBuffer(Vbo);
        Texture.Dispose();
    }
}