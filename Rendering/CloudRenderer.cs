// Main cloud rendering class. Holds reference to the shaders, cloud texture, and VAO/VBO. Also, it has the function to move the clouds | DA | 2/21/26
using Silk.NET.OpenGL;

using VoxelEngine.Terrain;

namespace VoxelEngine.Rendering;

/// <summary>
/// Renders the flat, scrolling cloud layer that sits above the world. The clouds are a single large double-sided quad (top + bottom faces) positioned at a fixed world-space height (<see cref="WorldGenSettings.CloudHeight"/>) and textured with a tiling cloud texture that is scrolled horizontally over time to simulate wind. Owns its own shader, texture, and GL buffers.
/// </summary>
public class CloudRenderer : IDisposable
{
    // Half-width of the square cloud plane, in world units (the plane spans -R..+R on X and Z). Large enough that it stays under the far/fog plane regardless of render distance.
    private const float CLOUD_PLANE_RADIIUS = 512f;
    private const float SCROLL_SPEED = 0.06f; // world units per tick

    private Shader mShader;
    private Texture mCloudTexture;
    private uint mVAO;
    private uint mVBO;
    // Accumulated horizontal scroll offset (in ticks); advanced once per game tick via Tick().
    private float mCloudOffsetX;

    /// <summary>Loads the cloud shader/texture and builds the static cloud-plane mesh. Call once at startup.</summary>
    public void Init()
    {
        mShader = new Shader(File.ReadAllText("Shaders/CloudVert.glsl"), File.ReadAllText("Shaders/CloudFrag.glsl"));
        mCloudTexture = Texture.LoadFromFile("Resources/clouds.png", true);
        BuildMesh();
    }

    // Builds a flat double-sided quad (top face + bottom face, both wound so they're visible from below/above) centered at the origin on the XZ plane. Actual world-space height and horizontal offset are applied in the vertex shader via uniforms, not baked into these verts.
    private void BuildMesh()
    {
        float r = CLOUD_PLANE_RADIIUS;
        float[] vertices =
        [
            // Top face (normal +Y)
            -r, 0f, -r,
            -r, 0f, +r,
            +r, 0f, +r,
            +r, 0f, +r,
            +r, 0f, -r,
            -r, 0f, -r,

            // Bottom face (normal -Y)
            -r, 0f, -r,
            +r, 0f, -r,
            +r, 0f, +r,
            +r, 0f, +r,
            -r, 0f, +r,
            -r, 0f, -r,
        ];

        var gl = GlContext.Gl;
        mVAO = gl.GenVertexArray();
        mVBO = gl.GenBuffer();
        gl.BindVertexArray(mVAO);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVBO);
        // Static mesh: clouds never deform, only their shader uniforms (offset/height) change per frame.
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

        // Layout 0: vec3 position, tightly packed (no UVs baked into the mesh - UVs are derived from world position in the shader so the texture can be scrolled/tiled).
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(3 * sizeof(float)), 0);
        gl.BindVertexArray(0);
    }

    /// <summary>Advances the cloud scroll offset by one game tick. Called once per fixed tick, not per frame.</summary>
    public void Tick()
    {
        mCloudOffsetX += 1.0f;
    }

    /// <summary>Resets the scroll offset back to zero (e.g. when loading/resetting a world) so clouds don't jump.</summary>
    public void ResetOffset()
    {
        mCloudOffsetX = 0.0f;
    }

    /// <summary>
    /// Draws the cloud plane for the current frame. Computes cloud tint from time-of-day (<paramref name="dayFactor"/>) and blends the scroll offset with <paramref name="partialTick"/> for smooth motion between fixed ticks, then temporarily disables depth writes and back-face culling so both the top and bottom faces render correctly regardless of camera height.
    /// </summary>
    public void Render(Vector3 playerPos, WorldGenSettings settings, float dayFactor, float partialTick, Vector3 fogColor, float fogDist, Matrix4x4 view, Matrix4x4 proj)
    {
        // Interpolate offset by the fraction of a tick elapsed since the last fixed update, then convert from "ticks" to "world units" via SCROLL_SPEED, for smooth scrolling.
        float uvScrollU = (mCloudOffsetX + partialTick) * SCROLL_SPEED;

        // Darken/tint the cloud color toward black as dayFactor approaches 0 (night), with the blue channel kept slightly brighter at night (0.15 floor vs 0.1) for a cooler night look.
        float brightRG = dayFactor * .9f + .1f;
        float brightB = dayFactor * .85f + .15f;

        Vector3 modColor = new Vector3(
            settings.CloudColor.X * brightRG,
            settings.CloudColor.Y * brightRG,
            settings.CloudColor.Z * brightB
        );

        // Paradise-themed worlds skip the day/night tint entirely and use the raw configured color.
        if (settings.Theme == WorldTheme.Paradise)
            modColor = settings.CloudColor;

        var gl = GlContext.Gl;
        // Clouds shouldn't occlude/be occluded by depth-tested geometry behind them or write depth themselves (they're a translucent-looking backdrop), and both faces must be visible since the plane can be viewed from above or below - so disable culling for this draw only.
        gl.DepthMask(false);
        gl.Disable(EnableCap.CullFace);

        mShader.Use();
        mShader.SetMatrix4("view", view);
        mShader.SetMatrix4("projection", proj);
        mShader.SetVector3("playerPos", playerPos);
        mShader.SetFloat("cloudPlaneY", settings.CloudHeight);
        mShader.SetFloat("uvScrollU", uvScrollU);
        mShader.SetVector3("cloudColor", modColor);
        mShader.SetInt("cloudTexture", 0);

        mShader.SetVector3("fogColor", fogColor);
        // Fog band is a fraction of total render/fog distance so clouds fade out before hitting the far plane.
        mShader.SetFloat("fogStart", fogDist * 0.4f);
        mShader.SetFloat("fogEnd", fogDist * 0.9f);

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, mCloudTexture.Handle);

        gl.BindVertexArray(mVAO);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 12); // 12 verts = 2 faces × 2 tris × 3 verts
        gl.BindVertexArray(0);

        // Restore GL State (depth writes + back-face culling) so subsequent renderers aren't affected.
        gl.DepthMask(true);
        gl.Enable(EnableCap.CullFace);
    }

    /// <summary>Releases the GL buffers, shader, and texture owned by this renderer.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVAO);
        gl.DeleteBuffer(mVBO);
        mShader.Dispose();
        mCloudTexture.Dispose();
    }
}
