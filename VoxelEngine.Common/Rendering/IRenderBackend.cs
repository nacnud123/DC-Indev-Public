// The GL seam. Common owns geometry and animation; the client owns the GPU. | DA

namespace VoxelEngine.Rendering;

/// <summary>
/// An opaque GPU resource - a model, an ad-hoc mesh, or a texture. Common code holds these,
/// passes them back to the backend, and disposes them, but can never look inside: the concrete
/// type is a client-side class wrapping GL handles.
///
/// This is what the build guide calls a "render handle". It's the reason <c>Chunk</c>, mobs, and
/// dropped items can stay in Common while their pixels stay in the client.
/// </summary>
public interface IRenderHandle : IDisposable
{
}

/// <summary>
/// Every GPU operation shared code performs, and nothing more.
///
/// The rule this enforces: Common decides *what* to draw and where (vertex data, transforms,
/// animation state); the client decides *how* (shaders, buffers, texture units). A dedicated
/// server binds <see cref="NullRenderBackend"/> and runs the identical simulation code with
/// every one of these calls going nowhere.
///
/// Vertex layout for <see cref="CreateMesh"/> is the entity shader's: position(3), uv(2),
/// normal(3) - 8 floats per vertex. Callers build that array themselves; only the upload is here.
/// </summary>
public interface IRenderBackend
{
    // ---- resource creation -------------------------------------------------------------

    /// <summary>Loads an .obj + texture pair as a single drawable model. Null if unavailable.</summary>
    IRenderHandle? LoadModel(string modelPath, string texturePath);

    /// <summary>Loads a multi-material .obj as one drawable model per material.</summary>
    IRenderHandle[] LoadModelsWithMtl(string modelPath, string mtlPath);

    /// <summary>Uploads an interleaved vertex array (pos3/uv2/normal3) as a static mesh.</summary>
    IRenderHandle? CreateMesh(float[] vertices, int vertexCount);

    /// <summary>Loads a standalone texture from disk.</summary>
    IRenderHandle? LoadTexture(string path);

    /// <summary>The terrain atlas (world.png). Null before the client has loaded it.</summary>
    IRenderHandle? WorldAtlas { get; }

    /// <summary>The item atlas (Items.png). Null before the client has loaded it.</summary>
    IRenderHandle? ItemAtlas { get; }

    /// <summary>Per-pixel atlas opacity plus tile size, for extruding a held item's icon into a
    /// thick sprite. Null before the client has loaded that atlas.</summary>
    (byte[,] Alpha, int TilePixels)? GetAtlasAlpha(bool itemAtlas);

    // ---- entity drawing ----------------------------------------------------------------

    /// <summary>
    /// Binds the shared entity shader and sets the per-entity uniforms that don't vary between
    /// an entity's parts (lighting, hit flash). Called once per entity, before its DrawModel.
    /// </summary>
    void BeginEntity(float hitFlash, float skyLight, float blockLight);

    void SetFloat(string name, float value);
    void SetMatrix4(string name, Matrix4x4 value);

    /// <summary>Draws a model loaded by <see cref="LoadModel"/>, which carries its own texture.</summary>
    void DrawModel(IRenderHandle model, Matrix4x4 mvp);

    /// <summary>
    /// Draws a mesh from <see cref="CreateMesh"/> against an explicitly supplied texture.
    /// <paramref name="doubleSided"/> disables backface culling for the duration (arrow fletching).
    /// </summary>
    void DrawMesh(IRenderHandle mesh, IRenderHandle? texture, Matrix4x4 mvp, bool doubleSided = false);

    /// <summary>Draws the shared flame billboard for a burning entity at the given transform.</summary>
    void DrawFireBillboard(Matrix4x4 mvp);

    // ---- chunk meshes ------------------------------------------------------------------
    //
    // Chunks keep their GL names as plain uints in Common - a uint is just a number, and
    // threading a handle object through the chunk hot path would allocate for no benefit.

    /// <summary>Creates or refills a chunk's VAO/VBO from freshly built vertex data.</summary>
    void UploadChunkMesh(ref uint vao, ref uint vbo, ref bool initialized, List<float> vertices, int vertexStride);

    /// <summary>Draws a chunk mesh previously uploaded by <see cref="UploadChunkMesh"/>.</summary>
    void DrawChunkMesh(uint vao, int vertexCount);

    /// <summary>Releases a chunk's VAO/VBO.</summary>
    void DeleteChunkMesh(uint vao, uint vbo);

    // ---- lifetime ----------------------------------------------------------------------

    /// <summary>Releases the shared entity shader and flame billboard.</summary>
    void DisposeEntityShader();
}

/// <summary>
/// Does nothing, successfully.
///
/// This is what makes a headless host work without a single null check in shared code: the
/// dedicated server leaves this bound, every draw call becomes a no-op, and resource loads
/// return null handles that nothing subsequently dereferences. It also means Common is
/// runnable in isolation, which is what a unit test would want.
/// </summary>
public sealed class NullRenderBackend : IRenderBackend
{
    public IRenderHandle? LoadModel(string modelPath, string texturePath) => null;
    public IRenderHandle[] LoadModelsWithMtl(string modelPath, string mtlPath) => Array.Empty<IRenderHandle>();
    public IRenderHandle? CreateMesh(float[] vertices, int vertexCount) => null;
    public IRenderHandle? LoadTexture(string path) => null;

    public IRenderHandle? WorldAtlas => null;
    public IRenderHandle? ItemAtlas => null;
    public (byte[,] Alpha, int TilePixels)? GetAtlasAlpha(bool itemAtlas) => null;

    public void BeginEntity(float hitFlash, float skyLight, float blockLight) { }
    public void SetFloat(string name, float value) { }
    public void SetMatrix4(string name, Matrix4x4 value) { }
    public void DrawModel(IRenderHandle model, Matrix4x4 mvp) { }
    public void DrawMesh(IRenderHandle mesh, IRenderHandle? texture, Matrix4x4 mvp, bool doubleSided = false) { }
    public void DrawFireBillboard(Matrix4x4 mvp) { }

    public void UploadChunkMesh(ref uint vao, ref uint vbo, ref bool initialized, List<float> vertices, int vertexStride) { }
    public void DrawChunkMesh(uint vao, int vertexCount) { }
    public void DeleteChunkMesh(uint vao, uint vbo) { }

    public void DisposeEntityShader() { }
}

/// <summary>
/// The currently bound backend. Defaults to <see cref="NullRenderBackend"/>, which is why the
/// build guide's suggested <c>if (GlContext.Gl == null) return;</c> guards aren't needed here -
/// a host that never binds a backend simply draws nothing, rather than crashing or needing every
/// call site to check.
/// </summary>
public static class RenderBackend
{
    /// <summary>Never null. The client swaps in a GL backend once its context exists.</summary>
    public static IRenderBackend Current { get; private set; } = new NullRenderBackend();

    /// <summary>True once a real (non-null) backend is bound. For code that wants to skip work entirely.</summary>
    public static bool HasGpu { get; private set; }

    public static void Bind(IRenderBackend backend)
    {
        Current = backend;
        HasGpu = backend is not NullRenderBackend;
    }

    /// <summary>Drops back to the no-op backend. Called on shutdown, after GL is gone.</summary>
    public static void Unbind()
    {
        Current = new NullRenderBackend();
        HasGpu = false;
    }
}
