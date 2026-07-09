// Pre-renders 3D isometric block icons to FBO textures for inventory/hotbar display | DA | 2/16/26
using Silk.NET.OpenGL;

using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Rendering;

/// <summary>
/// Pre-renders a small isometric 3D icon for every inventory-visible block type, one time at startup, into offscreen FBO textures that are then displayed by ImGui (inventory/hotbar UI) as plain 2D images. This avoids having to render actual 3D block geometry every UI frame. Handles three icon shapes: full cubes (Normal/Slab render types), stairs, and flat sprites (cross-shaped/flat blocks like flowers).
/// </summary>
public class BlockIconRenderer : IDisposable
{
    // Pixel width/height of each generated icon texture.
    private const int ICON_SIZE = 64;

    private const string VERT_SHADER = "Shaders/BlockIconVert.glsl";
    private const string FRAG_SHADER = "Shaders/BlockIconFrag.glsl";

    // Per-face brightness multipliers used to fake ambient occlusion / directional lighting on the isometric cube icons, since there's no real lighting pass for this offscreen render. Top is brightest (facing the "sun"), bottom darkest, sides in between - mimics how block faces are shaded in the main world renderer.
    private const float SHADE_TOP    = 1.0f;
    private const float SHADE_BOTTOM = 0.5f;
    private const float SHADE_FRONT  = 0.8f;
    private const float SHADE_BACK   = 0.8f;
    private const float SHADE_RIGHT  = 0.7f;
    private const float SHADE_LEFT   = 0.7f;

    // Maps each block type to the GL texture handle of its pre-rendered icon; populated once in Init().
    private readonly Dictionary<BlockType, uint> mIconTextures = new();
    private Shader mShader = null!;
    private uint mVao, mVbo;
    private bool mDisposed;

    /// <summary>
    /// Renders one icon texture per inventory-visible block type by drawing a small 3D mesh (cube, stair, or flat sprite depending on the block's render type) into a temporary FBO at <see cref="ICON_SIZE"/> resolution, saving each result as a persistent texture in <see cref="mIconTextures"/>. Must be called once after the world texture atlas is loaded and before any UI code calls <see cref="GetIcon"/>. Carefully saves and restores the previously bound framebuffer/viewport/shader program so it doesn't disturb whatever render state the caller had active.
    /// </summary>
    public void Init(Texture worldTexture)
    {
        var gl = GlContext.Gl;

        mShader = new Shader(File.ReadAllText(VERT_SHADER), File.ReadAllText(FRAG_SHADER));

        mVao = gl.GenVertexArray();
        mVbo = gl.GenBuffer();
        gl.BindVertexArray(mVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
        // position(3) + uv(2) + shade(1) = 6 floats/vertex
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(6 * sizeof(float)), 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(6 * sizeof(float)), (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 1, GLEnum.Float, false, (uint)(6 * sizeof(float)), (nint)(5 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        // One shared depth renderbuffer reused for every icon (only color attachment changes per-block below).
        uint fbo = gl.GenFramebuffer();
        uint depthRbo = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRbo);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, ICON_SIZE, ICON_SIZE);

        // Save the caller's current framebuffer/viewport/shader program so they can be restored after this offscreen batch - this method is designed to be callable without disturbing whatever the main renderer had bound (important since Init runs once at startup, but defensively avoids leaking GL state either way).
        gl.GetInteger(GetPName.DrawFramebufferBinding, out int prevFbo);
        int[] viewport = new int[4];
        unsafe { fixed (int* pv = viewport) { gl.GetInteger(GetPName.Viewport, pv); } }
        gl.GetInteger(GetPName.CurrentProgram, out int prevProgram);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, depthRbo);

        // Shared isometric-style MVP for 3D icons (cubes/stairs); flat sprites use an identity MVP instead since they're drawn face-on with pre-defined [-1,1] quad coordinates.
        var mvp = BuildMvp();
        var flatMvp = Matrix4x4.Identity;

        foreach (var block in BlockRegistry.GetAll())
        {
            if (!block.ShowInInventory) continue;

            bool isCube = block.RenderType == RenderingType.Normal
                       || block.RenderType == RenderingType.Slab;

            // Each block gets its own persistent color texture (kept alive for the lifetime of this renderer) that becomes this block's icon; re-attached to the shared FBO below.
            uint colorTex = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, colorTex);
            unsafe
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, ICON_SIZE, ICON_SIZE, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, null);
            }
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, colorTex, 0);

            gl.Viewport(0, 0, ICON_SIZE, ICON_SIZE);
            // Clear to fully transparent so icons composite cleanly over ImGui's UI background.
            gl.ClearColor(0f, 0f, 0f, 0f);
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            gl.Enable(EnableCap.DepthTest);
            // Culling off: icon meshes are small and drawn from a single fixed angle, so it's simpler/safer to just draw both winding orders than get face culling exactly right for every block shape (cubes, stairs, flipped-Y stair top half, flat sprites).
            gl.Disable(EnableCap.CullFace);
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            mShader.Use();

            worldTexture.Use(TextureUnit.Texture0);
            mShader.SetInt("tex", 0);

            float[] vertices;
            if (isCube)
            {
                mShader.SetMatrix4("mvp", mvp);
                vertices = BuildCubeMesh(block);
            }
            else if (block.RenderType == RenderingType.Stair)
            {
                mShader.SetMatrix4("mvp", mvp);
                vertices = BuildStairMesh(block);
            }
            else
            {
                // Flat/cross-type blocks (flowers, saplings, etc.) get a simple front-facing quad instead of 3D geometry, using the identity MVP so the [-1,1] quad fills the icon directly.
                mShader.SetMatrix4("mvp", flatMvp);
                vertices = BuildFlatSprite(block.InventoryTextureCoords);
            }

            gl.BindVertexArray(mVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVbo);
            // Re-upload this block's mesh into the shared VBO each iteration (vertex count/shape varies per block/render type, so it can't be a fixed static buffer).
            gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.DynamicDraw);
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(vertices.Length / 6));

            gl.BindVertexArray(0);

            mIconTextures[block.Type] = colorTex;
        }

        // Restore whatever framebuffer/viewport/shader program was bound before Init() ran.
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)prevFbo);
        gl.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
        gl.UseProgram((uint)prevProgram);
        gl.Enable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);

        // The shared depth renderbuffer and temporary FBO are only needed during icon generation; the per-block color textures they rendered into are kept (see mIconTextures) and outlive this cleanup.
        gl.DeleteRenderbuffer(depthRbo);
        gl.DeleteFramebuffer(fbo);
    }

    /// <summary>
    /// Returns the pre-rendered icon texture handle for a block type, packed as an <see cref="IntPtr"/> the way ImGui.NET's <c>Image()</c> calls expect a texture id. Returns <see cref="IntPtr.Zero"/> if the block has no icon (e.g. <see cref="Init"/> hasn't run yet, or the block isn't inventory-visible).
    /// </summary>
    public IntPtr GetIcon(BlockType type)
    {
        if (mIconTextures.TryGetValue(type, out uint handle))
            return new IntPtr(handle);

        return IntPtr.Zero;
    }

    /// <summary>
    /// Builds the fixed isometric-style camera transform used to render 3D block icons: an orthographic projection (no perspective distortion, matching Minecraft-style inventory icons) looking at a unit cube centered at the origin, tilted -30 deg on X and rotated 45 deg on Y to show the top, front, and right faces simultaneously.
    /// </summary>
    private static Matrix4x4 BuildMvp()
    {
        // Half-extent of the orthographic view volume; 1.1 gives a small margin around the unit cube (which after centering spans roughly [-0.87, 0.87] at this rotation) so it isn't clipped.
        float orthoSize = 1.1f;
        var projection = Matrix4x4.CreateOrthographic(orthoSize * 2f, orthoSize * 2f, 0.1f, 10f);
        // Recenter the cube (block-space [0,1]) on the origin before rotating, so it spins in place rather than orbiting around a corner.
        var translate  = Matrix4x4.CreateTranslation(-0.5f, -0.5f, -0.5f);
        // -30 deg pitch tilts the camera to look down at the top face; 45 deg yaw shows two side faces - the classic isometric inventory-icon angle.
        var rotationX  = Matrix4x4.CreateRotationX(float.DegreesToRadians(-30f));
        var rotationY  = Matrix4x4.CreateRotationY(float.DegreesToRadians(45f));
        var view       = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        // Order matters: translate to origin first, then rotate around Y (yaw), then X (pitch), matching the multiplication order used everywhere else for row-vector transforms.
        var model      = translate * rotationY * rotationX;
        return model * view * projection;
    }

    /// <summary>
    /// Builds a single front-facing quad spanning [-1,1] on X/Y at Z=0, used as the icon mesh for flat/cross-shaped blocks (flowers, saplings) which don't have real cube geometry to render.
    /// </summary>
    private static float[] BuildFlatSprite(TextureCoords tex)
    {
        var vertices = new List<float>();
        float shade = 1.0f;
        AddQuad(vertices,
            new Vector3(-1, -1, 0), new Vector3(-1, 1, 0),
            new Vector3(1, 1, 0), new Vector3(1, -1, 0),
            tex, shade);
        return vertices.ToArray();
    }

    /// <summary>Builds a full unit-cube mesh (6 faces) using the block's per-face texture coords and bounds.</summary>
    private static float[] BuildCubeMesh(Block block)
    {
        var min = block.BoundsMin;
        var max = block.BoundsMax;

        var vertices = new List<float>();

        AddBox(vertices, min, max,
            block.BottomTextureCoords, block.TopTextureCoords,
            block.FrontTextureCoords, block.BackTextureCoords,
            block.RightTextureCoords, block.LeftTextureCoords);

        return vertices.ToArray();
    }

    /// <summary>
    /// Builds a stair-shaped icon mesh out of two stacked boxes (a full-depth bottom slab and a half-depth top slab, mimicking a single stair step), then mirrors the whole mesh across the Y and Z center planes to orient the step correctly for the icon's fixed camera angle.
    /// </summary>
    private static float[] BuildStairMesh(Block block)
    {
        var min = block.BoundsMin;
        var max = block.BoundsMax;

        var vertices = new List<float>();

        // Bottom box: full X/Z footprint, lower half height only (the stair's base slab).
        var b0min = new Vector3(min.X, min.Y, min.Z);
        var b0max = new Vector3(max.X, min.Y + 0.5f * (max.Y - min.Y), max.Z);
        AddBox(vertices, b0min, b0max,
            block.TopTextureCoords, block.BottomTextureCoords,
            block.FrontTextureCoords, block.BackTextureCoords,
            block.RightTextureCoords, block.LeftTextureCoords);

        // Top box: upper half height, and only the back half of Z depth - this is the raised "step" part of the stair sitting on the back half of the bottom box.
        var b1min = new Vector3(min.X, min.Y + 0.5f * (max.Y - min.Y), min.Z + 0.5f * (max.Z - min.Z));
        var b1max = new Vector3(max.X, max.Y, max.Z);
        AddBox(vertices, b1min, b1max,
            block.TopTextureCoords, block.BottomTextureCoords,
            block.FrontTextureCoords, block.BackTextureCoords,
            block.RightTextureCoords, block.LeftTextureCoords);

        // Mirror every vertex's Y and Z about the block center (0.5, 0.5). This flips the two stacked boxes built above into the actual stair silhouette (step raised toward the camera/front rather than the back) without needing a second, differently-shaped mesh builder.
        float centerY = 0.5f;
        float centerZ = 0.5f;
        for (int i = 0; i < vertices.Count; i += 6)
        {
            vertices[i + 1] = 2f * centerY - vertices[i + 1];
            vertices[i + 2] = 2f * centerZ - vertices[i + 2];
        }

        return vertices.ToArray();
    }

    /// <summary>
    /// Emits 6 quads (one per axis-aligned face) forming a box from <paramref name="min"/> to <paramref name="max"/>, each face using its own texture coords and a fixed directional shade constant (see the SHADE_* constants) to fake simple per-face lighting.
    /// </summary>
    private static void AddBox(List<float> vertices, Vector3 min, Vector3 max,
        TextureCoords top, TextureCoords bottom,
        TextureCoords front, TextureCoords back,
        TextureCoords right, TextureCoords left)
    {
        AddQuad(vertices,
            new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z),
            top, SHADE_TOP);

        AddQuad(vertices,
            new Vector3(min.X, min.Y, max.Z), new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, min.Z), new Vector3(min.X, min.Y, min.Z),
            bottom, SHADE_BOTTOM);

        AddQuad(vertices,
            new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z), new Vector3(max.X, min.Y, max.Z),
            front, SHADE_FRONT);

        AddQuad(vertices,
            new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z), new Vector3(min.X, min.Y, min.Z),
            back, SHADE_BACK);

        AddQuad(vertices,
            new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, min.Y, min.Z),
            right, SHADE_RIGHT);

        AddQuad(vertices,
            new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, min.Y, max.Z),
            left, SHADE_LEFT);
    }

    /// <summary>
    /// Triangulates a quad given four corner positions (v0..v3, expected in either clockwise or counter-clockwise winding around the face) into two triangles, mapping the quad corners to the texture's UV rectangle corners and tagging every vertex with the given shade value.
    /// </summary>
    private static void AddQuad(List<float> vertices,
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
        TextureCoords tex, float shade)
    {
        float u0 = tex.TopLeft.X, v0t = tex.TopLeft.Y;
        float u1 = tex.BottomRight.X, v1t = tex.BottomRight.Y;

        // Triangle 1: v0,v1,v2 - Triangle 2: v0,v2,v3 (standard quad-to-2-triangle fan split).
        AddVertex(vertices, v0, u0, v1t, shade);
        AddVertex(vertices, v1, u0, v0t, shade);
        AddVertex(vertices, v2, u1, v0t, shade);

        AddVertex(vertices, v0, u0, v1t, shade);
        AddVertex(vertices, v2, u1, v0t, shade);
        AddVertex(vertices, v3, u1, v1t, shade);
    }

    /// <summary>Appends one interleaved vertex (position, uv, shade) - matches the 6-float vertex layout bound in <see cref="Init"/>.</summary>
    private static void AddVertex(List<float> vertices, Vector3 pos, float u, float v, float shade)
    {
        vertices.Add(pos.X);
        vertices.Add(pos.Y);
        vertices.Add(pos.Z);
        vertices.Add(u);
        vertices.Add(v);
        vertices.Add(shade);
    }

    /// <summary>Releases every generated icon texture, the shared mesh VAO/VBO, and the icon shader. Safe to call multiple times.</summary>
    public void Dispose()
    {
        if (mDisposed) return;
        mDisposed = true;

        var gl = GlContext.Gl;
        foreach (var tex in mIconTextures.Values)
            gl.DeleteTexture(tex);
        mIconTextures.Clear();

        if (mVbo != 0) gl.DeleteBuffer(mVbo);
        if (mVao != 0) gl.DeleteVertexArray(mVao);
        mShader?.Dispose();
    }
}
