// Pre-renders 3D isometric block icons to FBO textures for inventory/hotbar display | DA | 2/16/26
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Rendering;

// Pre-renders block icons into FBO textures. Produces isometric 3D icons for Normal/Slab blocks and a composed stair mesh for Stairs. Icons are stored as OpenGL texture handles in-memory.
public class BlockIconRenderer : IDisposable
{
    private const int ICON_SIZE = 64;

    private static readonly string VertexShaderSrc = @"
#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUV;
layout(location=2) in float aShade;
out vec2 uv;
out float shade;
uniform mat4 mvp;
void main() {
    gl_Position = mvp * vec4(aPos, 1.0);
    uv = aUV;
    shade = aShade;
}";

    private static readonly string FragmentShaderSrc = @"
#version 330 core
in vec2 uv;
in float shade;
out vec4 FragColor;
uniform sampler2D tex;
void main() {
    vec4 c = texture(tex, uv);
    if (c.a < 0.1) discard;
    FragColor = vec4(c.rgb * shade, c.a);
}";

    // Face shading values (kept in sync with ChunkMeshBuilder shading)
    private const float SHADE_TOP = 1.0f;
    private const float SHADE_BOTTOM = 0.5f;
    private const float SHADE_FRONT = 0.8f;  // +Z
    private const float SHADE_BACK = 0.8f;   // -Z
    private const float SHADE_RIGHT = 0.7f;  // +X
    private const float SHADE_LEFT = 0.7f;   // -X

    private readonly Dictionary<BlockType, int> mIconTextures = new();
    private Shader mShader = null!;
    private int mVao, mVbo;
    private bool mDisposed;

    public void Init(Texture worldTexture)
    {
        mShader = new Shader(VertexShaderSrc, FragmentShaderSrc);

        mVao = GL.GenVertexArray();
        mVbo = GL.GenBuffer();
        GL.BindVertexArray(mVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        // position(3) + uv(2) + shade(1) = 6 floats/vertex
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 6 * sizeof(float), 5 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.BindVertexArray(0);

        int fbo = GL.GenFramebuffer();
        int depthRbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRbo);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, ICON_SIZE, ICON_SIZE);

        GL.GetInteger(GetPName.FramebufferBinding, out int prevFbo);
        int[] viewport = new int[4];
        GL.GetInteger(GetPName.Viewport, viewport);
        GL.GetInteger(GetPName.CurrentProgram, out int prevProgram);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, depthRbo);

        var mvp = BuildMvp();
        var flatMvp = Matrix4.Identity;

        foreach (var block in BlockRegistry.GetAll())
        {
            if (!block.ShowInInventory) continue;

            // Normal/Slab → 3D cube; Stair → composed stair mesh; everything else → flat sprite.
            bool isCube = block.RenderType == RenderingType.Normal
                       || block.RenderType == RenderingType.Slab;

            // Color texture is kept alive after the FBO is deleted; only the FBO and depth RBO are temporary.
            int colorTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, colorTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, ICON_SIZE, ICON_SIZE, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, colorTex, 0);

            GL.Viewport(0, 0, ICON_SIZE, ICON_SIZE);
            GL.ClearColor(0f, 0f, 0f, 0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

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
                // Build a small 3D stair mesh (two boxes) and render with the same isometric MVP.
                mShader.SetMatrix4("mvp", mvp);
                vertices = BuildStairMesh(block);
            }
            else
            {
                mShader.SetMatrix4("mvp", flatMvp);
                vertices = BuildFlatSprite(block.InventoryTextureCoords);
            }

            GL.BindVertexArray(mVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertices.Length / 6);

            GL.BindVertexArray(0);

            mIconTextures[block.Type] = colorTex;
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
        GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
        GL.UseProgram(prevProgram);
        GL.Enable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);

        GL.DeleteRenderbuffer(depthRbo);
        GL.DeleteFramebuffer(fbo);
    }

    public IntPtr GetIcon(BlockType type)
    {
        if (mIconTextures.TryGetValue(type, out int handle))
            return new IntPtr(handle);

        return IntPtr.Zero;
    }

    private static Matrix4 BuildMvp()
    {
        float orthoSize = 1.1f;
        var projection = Matrix4.CreateOrthographic(orthoSize * 2f, orthoSize * 2f, 0.1f, 10f);
        var translate  = Matrix4.CreateTranslation(-0.5f, -0.5f, -0.5f);
        var rotationX  = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(-30f));
        var rotationY  = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(45f));
        var view       = Matrix4.LookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        var model      = translate * rotationY * rotationX;
        return model * view * projection;
    }

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

    private static float[] BuildCubeMesh(Block block)
    {
        var min = block.BoundsMin;
        var max = block.BoundsMax;

        var vertices = new List<float>();

        // Top/bottom are swapped in AddBox's winding — pass them reversed so the visual top uses TopTextureCoords.
        AddBox(vertices, min, max,
            block.BottomTextureCoords, block.TopTextureCoords,
            block.FrontTextureCoords, block.BackTextureCoords,
            block.RightTextureCoords, block.LeftTextureCoords);

        return vertices.ToArray();
    }

    // Build a compact stair icon from two axis-aligned boxes (bottom half + step).
    private static float[] BuildStairMesh(Block block)
    {
        var min = block.BoundsMin;
        var max = block.BoundsMax;

        var vertices = new List<float>();

        var b0min = new Vector3(min.X, min.Y, min.Z);
        var b0max = new Vector3(max.X, min.Y + 0.5f * (max.Y - min.Y), max.Z);
        AddBox(vertices, b0min, b0max,
            block.TopTextureCoords, block.BottomTextureCoords,
            block.FrontTextureCoords, block.BackTextureCoords,
            block.RightTextureCoords, block.LeftTextureCoords);

        var b1min = new Vector3(min.X, min.Y + 0.5f * (max.Y - min.Y), min.Z + 0.5f * (max.Z - min.Z));
        var b1max = new Vector3(max.X, max.Y, max.Z);
        AddBox(vertices, b1min, b1max,
            block.TopTextureCoords, block.BottomTextureCoords,
            block.FrontTextureCoords, block.BackTextureCoords,
            block.RightTextureCoords, block.LeftTextureCoords);

        // The stair mesh is built upside-down due to winding; flip 180 degrees around X (center 0.5, 0.5) to correct it.
        float centerY = 0.5f;
        float centerZ = 0.5f;
        for (int i = 0; i < vertices.Count; i += 6)
        {
            // (x, y, z) -> (x, 1-y, 1-z) about center (0.5, 0.5)
            vertices[i + 1] = 2f * centerY - vertices[i + 1];
            vertices[i + 2] = 2f * centerZ - vertices[i + 2];
        }

        return vertices.ToArray();
    }

    // Add an axis-aligned box (6 faces) with per-face textures.
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

    // Two CCW triangles (v0,v1,v2) + (v0,v2,v3); UVs from TextureCoords.TopLeft / BottomRight.
    private static void AddQuad(List<float> vertices,
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
        TextureCoords tex, float shade)
    {
        float u0 = tex.TopLeft.X, v0t = tex.TopLeft.Y;
        float u1 = tex.BottomRight.X, v1t = tex.BottomRight.Y;

        AddVertex(vertices, v0, u0, v1t, shade);
        AddVertex(vertices, v1, u0, v0t, shade);
        AddVertex(vertices, v2, u1, v0t, shade);

        AddVertex(vertices, v0, u0, v1t, shade);
        AddVertex(vertices, v2, u1, v0t, shade);
        AddVertex(vertices, v3, u1, v1t, shade);
    }

    private static void AddVertex(List<float> vertices, Vector3 pos, float u, float v, float shade)
    {
        vertices.Add(pos.X);
        vertices.Add(pos.Y);
        vertices.Add(pos.Z);
        vertices.Add(u);
        vertices.Add(v);
        vertices.Add(shade);
    }

    public void Dispose()
    {
        if (mDisposed) return;
        mDisposed = true;

        foreach (var tex in mIconTextures.Values)
            GL.DeleteTexture(tex);
        mIconTextures.Clear();

        if (mVbo != 0) GL.DeleteBuffer(mVbo);
        if (mVao != 0) GL.DeleteVertexArray(mVao);
        mShader?.Dispose();
    }
}