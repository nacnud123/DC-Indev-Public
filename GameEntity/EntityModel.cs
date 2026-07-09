// Main file for entity model, it holds reference to GL stuff like Vao, Vbo, Texture, and VertexCount | DA | 2/5/26

using Silk.NET.OpenGL;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

/// <summary>
/// A single GPU-uploaded mesh + texture pair used to render an entity (or one material-grouped part of a multi-material entity model). Wraps a VAO/VBO holding interleaved position/uv/normal vertex data (see <see cref="ObjLoader.FLOATS_PER_VERTEX"/> for the stride). Models are loaded from Wavefront .obj files (optionally with a .mtl material file for multi-part/multi-texture models) and cached by path so repeated loads of the same model+texture combo reuse the GPU resources instead of re-uploading.
/// </summary>
public class EntityModel : IDisposable
{
    // Cache for single-texture models loaded via Load().
    private static readonly Dictionary<string, EntityModel> Cache = new();
    // Cache for multi-part, multi-texture models loaded via LoadWithMtl() - one array entry per material group.
    private static readonly Dictionary<string, EntityModel[]> MtlCache = new();

    public uint Vao { get; }
    public uint Vbo { get; }
    public int VertexCount { get; }
    public Texture Texture { get; }

    private EntityModel(uint vao, uint vbo, int vertexCount, Texture texture)
    {
        Vao = vao;
        Vbo = vbo;
        VertexCount = vertexCount;
        Texture = texture;
    }

    // Loads (or returns the cached instance of) a single-texture .obj model. Used for simple mob models that only need one texture for the whole mesh.
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

    // Loads (or returns the cached array for) a multi-material .obj model: the mesh is split into one EntityModel per material group (each with its own texture, resolved via the .mtl file), so the caller draws the whole model as a sequence of parts. Groups with no vertices, or whose material has no resolvable texture, are skipped.
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

    // Creates the VAO/VBO and uploads interleaved vertex data (pos/uv/normal), wiring up the same three vertex attributes (location 0/1/2) used across the engine's other mesh builders.
    private static EntityModel Upload(float[] vertices, int vertexCount, Texture texture)
    {
        var gl = GlContext.Gl;
        uint vao = gl.GenVertexArray();
        uint vbo = gl.GenBuffer();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

        uint stride = (uint)(ObjLoader.FLOATS_PER_VERTEX * sizeof(float));
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, 0); // position (3 floats)
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (nint)(3 * sizeof(float))); // uv (2 floats)
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, (nint)(5 * sizeof(float))); // normal (3 floats)
        gl.EnableVertexAttribArray(2);

        gl.BindVertexArray(0);
        return new EntityModel(vao, vbo, vertexCount, texture);
    }

    // Frees every cached GPU resource (both single-texture and multi-material caches). Called on shutdown/context teardown to avoid leaking GL objects.
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
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(Vao);
        gl.DeleteBuffer(Vbo);
        Texture.Dispose();
    }
}
