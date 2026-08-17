// The client half of the render seam: everything Common asks for, done with real GL. | DA

using Silk.NET.OpenGL;
using StbImageSharp;

using VoxelEngine.GameEntity;
using VoxelEngine.Utils;

namespace VoxelEngine.Rendering;

/// <summary>
/// Wraps an <see cref="EntityModel"/> (an .obj mesh plus its own texture) as an opaque handle.
/// </summary>
internal sealed class ModelHandle : IRenderHandle
{
    public readonly EntityModel Model;
    public ModelHandle(EntityModel model) => Model = model;
    public void Dispose() => Model.Dispose();
}

/// <summary>
/// Wraps a standalone <see cref="Texture"/> as an opaque handle. Atlas handles are shared and
/// owned by <see cref="Game"/>, so <see cref="Dispose"/> is deliberately conditional.
/// </summary>
internal sealed class TextureHandle : IRenderHandle
{
    public readonly Texture Texture;
    private readonly bool mOwned;

    public TextureHandle(Texture texture, bool owned)
    {
        Texture = texture;
        mOwned = owned;
    }

    // Shared atlases outlive any single entity holding a handle to them - a dropped item being
    // collected must not delete the terrain atlas out from under the rest of the world.
    public void Dispose()
    {
        if (mOwned)
            Texture.Dispose();
    }
}

/// <summary>An ad-hoc VAO/VBO built from an interleaved vertex array.</summary>
internal sealed class MeshHandle : IRenderHandle
{
    public readonly uint Vao;
    public readonly uint Vbo;
    public readonly int VertexCount;

    public MeshHandle(uint vao, uint vbo, int vertexCount)
    {
        Vao = vao;
        Vbo = vbo;
        VertexCount = vertexCount;
    }

    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(Vao);
        gl.DeleteBuffer(Vbo);
    }
}

/// <summary>
/// The GL implementation of <see cref="IRenderBackend"/>. Owns the shared entity shader and the
/// flame billboard that used to live as statics on <c>Entity</c>.
/// </summary>
public sealed class GlRenderBackend : IRenderBackend
{
    /// <summary>
    /// The client's single backend instance.
    ///
    /// Constructing it is free - the shader and flame quad are both built lazily on first use, so
    /// this can exist before there's a GL context. Client-side renderers that draw inside the
    /// entity pass (PlayerArm, PaintingRenderer) reach the shared entity shader through here;
    /// they used to reach <c>Entity._shader</c>, which no longer exists in Common.
    /// </summary>
    public static GlRenderBackend Active { get; } = new();

    // Entity vertex layout: position(3) + uv(2) + normal(3).
    private const int ENTITY_STRIDE = 8;

    private Shader? mEntityShader;

    private uint mFireVao, mFireVbo;
    private bool mFireReady;

    private Texture? mWorldAtlas;
    private Texture? mItemAtlas;
    private TextureHandle? mWorldAtlasHandle;
    private TextureHandle? mItemAtlasHandle;

    private (byte[,] Alpha, int TilePixels)? mWorldAtlasAlpha;
    private (byte[,] Alpha, int TilePixels)? mItemAtlasAlpha;

    /// <summary>
    /// The shared entity shader, compiled on first use. Exposed because a few client-side
    /// renderers (PlayerArm, PaintingRenderer) draw into the same pass and need to set their own
    /// uniforms on it - they used to reach <c>Entity._shader</c> directly.
    /// </summary>
    public Shader EntityShader
    {
        get
        {
            mEntityShader ??= new Shader(
                File.ReadAllText("Shaders/EntityVertex.glsl"),
                File.ReadAllText("Shaders/EntityFragment.glsl"));
            return mEntityShader;
        }
    }

    /// <summary>Hands the backend the atlases once <c>Game</c> has loaded them.</summary>
    public void SetAtlases(Texture worldAtlas, Texture itemAtlas)
    {
        mWorldAtlas = worldAtlas;
        mItemAtlas = itemAtlas;
        mWorldAtlasHandle = new TextureHandle(worldAtlas, owned: false);
        mItemAtlasHandle = new TextureHandle(itemAtlas, owned: false);
    }

    // ---- resource creation -----------------------------------------------------------------

    public IRenderHandle? LoadModel(string modelPath, string texturePath)
        => new ModelHandle(EntityModel.Load(modelPath, texturePath));

    public IRenderHandle[] LoadModelsWithMtl(string modelPath, string mtlPath)
        => EntityModel.LoadWithMtl(modelPath, mtlPath)
                      .Select(m => (IRenderHandle)new ModelHandle(m))
                      .ToArray();

    public IRenderHandle? LoadTexture(string path)
        => new TextureHandle(Texture.LoadFromFile(path), owned: true);

    public IRenderHandle? WorldAtlas => mWorldAtlasHandle;
    public IRenderHandle? ItemAtlas => mItemAtlasHandle;

    // GL never keeps a CPU-side copy of a texture, so this loads the PNG again for its alpha channel.
    public (byte[,] Alpha, int TilePixels)? GetAtlasAlpha(bool itemAtlas)
    {
        if (itemAtlas)
            return mItemAtlasAlpha ??= LoadAtlasAlpha("Resources/Items.png");

        return mWorldAtlasAlpha ??= LoadAtlasAlpha("Resources/world.png");
    }

    private static (byte[,] Alpha, int TilePixels) LoadAtlasAlpha(string path)
    {
        StbImage.stbi_set_flip_vertically_on_load(0);
        ImageResult img;
        using (var s = File.OpenRead(path))
            img = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);
        StbImage.stbi_set_flip_vertically_on_load(1);

        var alpha = new byte[img.Width, img.Height];
        for (int row = 0; row < img.Height; row++)
        for (int col = 0; col < img.Width; col++)
        {
            int glRow = img.Height - 1 - row;
            alpha[col, glRow] = img.Data[(row * img.Width + col) * 4 + 3];
        }

        return (alpha, img.Width / UvHelper.TILE_COUNT);
    }

    public IRenderHandle? CreateMesh(float[] vertices, int vertexCount)
    {
        var gl = GlContext.Gl;
        uint vao = gl.GenVertexArray();
        uint vbo = gl.GenBuffer();

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

        uint stride = (uint)(ENTITY_STRIDE * sizeof(float));
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, (nint)(5 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        return new MeshHandle(vao, vbo, vertexCount);
    }

    // ---- entity drawing --------------------------------------------------------------------

    public void BeginEntity(float hitFlash, float skyLight, float blockLight)
    {
        var shader = EntityShader;
        shader.Use();
        // LightDir/AmbientStrength/SunlightLevel are frame constants GameRenderer writes onto
        // Entity before the entity pass; they stay there because they're plain data, not GL state.
        shader.SetVector3("lightDir", Entity.LightDir);
        shader.SetFloat("ambientStrength", Entity.AmbientStrength);
        shader.SetFloat("uHitFlash", hitFlash);
        shader.SetFloat("sunlightLevel", Entity.SunlightLevel);
        shader.SetFloat("skyLight", skyLight);
        shader.SetFloat("blockLight", blockLight);
    }

    public void SetFloat(string name, float value) => EntityShader.SetFloat(name, value);
    public void SetMatrix4(string name, Matrix4x4 value) => EntityShader.SetMatrix4(name, value);

    public void DrawModel(IRenderHandle model, Matrix4x4 mvp)
    {
        if (model is not ModelHandle handle)
            return;

        EntityShader.SetMatrix4("mvp", mvp);
        handle.Model.Texture.Use(TextureUnit.Texture0);

        var gl = GlContext.Gl;
        gl.BindVertexArray(handle.Model.Vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)handle.Model.VertexCount);
    }

    public void DrawMesh(IRenderHandle mesh, IRenderHandle? texture, Matrix4x4 mvp, bool doubleSided = false)
    {
        if (mesh is not MeshHandle handle)
            return;

        EntityShader.SetMatrix4("mvp", mvp);
        if (texture is TextureHandle tex)
            tex.Texture.Use(TextureUnit.Texture0);

        var gl = GlContext.Gl;
        if (doubleSided)
            gl.Disable(EnableCap.CullFace);

        gl.BindVertexArray(handle.Vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)handle.VertexCount);

        if (doubleSided)
            gl.Enable(EnableCap.CullFace);
    }

    public void DrawFireBillboard(Matrix4x4 mvp)
    {
        EnsureFireVao();

        EntityShader.SetMatrix4("mvp", mvp);
        mWorldAtlas?.Use(TextureUnit.Texture0);

        var gl = GlContext.Gl;
        gl.BindVertexArray(mFireVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    // One quad shared by every burning entity, built on first use.
    private void EnsureFireVao()
    {
        if (mFireReady)
            return;

        var fireUv = UvHelper.FromTileCoords(6, 7);
        float u0 = fireUv.TopLeft.X,     v0 = fireUv.TopLeft.Y;
        float u1 = fireUv.BottomRight.X, v1 = fireUv.BottomRight.Y;

        float[] verts =
        {
            -0.5f, 0f, 0f,  u0, v0,  0f, 0f, 1f,
             0.5f, 0f, 0f,  u1, v0,  0f, 0f, 1f,
             0.5f, 1f, 0f,  u1, v1,  0f, 0f, 1f,

            -0.5f, 0f, 0f,  u0, v0,  0f, 0f, 1f,
             0.5f, 1f, 0f,  u1, v1,  0f, 0f, 1f,
            -0.5f, 1f, 0f,  u0, v1,  0f, 0f, 1f,
        };

        var gl = GlContext.Gl;
        mFireVao = gl.GenVertexArray();
        mFireVbo = gl.GenBuffer();
        gl.BindVertexArray(mFireVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mFireVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts, BufferUsageARB.StaticDraw);

        uint stride = (uint)(ENTITY_STRIDE * sizeof(float));
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, (nint)(5 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        mFireReady = true;
    }

    // ---- chunk meshes ----------------------------------------------------------------------

    public unsafe void UploadChunkMesh(ref uint vao, ref uint vbo, ref bool initialized,
                                       List<float> vertices, int vertexStride)
    {
        var gl = GlContext.Gl;
        if (!initialized)
        {
            vao = gl.GenVertexArray();
            vbo = gl.GenBuffer();
            initialized = true;
        }

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer,
                             System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices),
                             BufferUsageARB.DynamicDraw);

        // Chunk layout differs from the entity layout:
        // position(3) + [skyLight, blockLight, faceShade](3) + normal(3) + uv(2) + fluidAnim(1)
        // = 12 floats. fluidAnim is 1 on flowing fluid vertices and 0 on everything else, which is
        // how the fragment shader knows to scroll a stream's texture but leave a still pond alone.
        uint stride = (uint)(vertexStride * sizeof(float));
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (nint)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, GLEnum.Float, false, stride, (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, (nint)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(3, 2, GLEnum.Float, false, stride, (nint)(9 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(4, 1, GLEnum.Float, false, stride, (nint)(11 * sizeof(float)));
        gl.EnableVertexAttribArray(4);

        gl.BindVertexArray(0);
    }

    public void DrawChunkMesh(uint vao, int vertexCount)
    {
        var gl = GlContext.Gl;
        gl.BindVertexArray(vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
    }

    public void DeleteChunkMesh(uint vao, uint vbo)
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(vao);
        gl.DeleteBuffer(vbo);
    }

    // ---- lifetime --------------------------------------------------------------------------

    public void DisposeEntityShader()
    {
        mEntityShader?.Dispose();
        mEntityShader = null;

        if (mFireReady)
        {
            var gl = GlContext.Gl;
            gl.DeleteVertexArray(mFireVao);
            gl.DeleteBuffer(mFireVbo);
            mFireReady = false;
        }
    }
}
