// Main cloud rendering class. Holds reference to the shaders, cloud texture, and VAO/VBO. Also, it has the function to move the clouds | DA | 2/21/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Terrain;

namespace VoxelEngine.Rendering;

public class CloudRenderer : IDisposable
{
    private const float CLOUD_PLANE_RADIIUS = 512f;
    private const float SCROLL_SPEED = 0.06f; // world units per tick

    private Shader mShader;
    private Texture mCloudTexture;
    private int mVAO;
    private int mVBO;
    private float mCloudOffsetX;

    public void Init()
    {
        mShader = new Shader(File.ReadAllText("Shaders/CloudVert.glsl"), File.ReadAllText("Shaders/CloudFrag.glsl"));
        mCloudTexture = Texture.LoadFromFile("Resources/clouds.png", true);
        BuildMesh();
    }

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

        mVAO = GL.GenVertexArray();
        mVBO = GL.GenBuffer();
        GL.BindVertexArray(mVAO);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVBO);
        GL.BufferData(BufferTarget.ArrayBuffer,
            vertices.Length * sizeof(float),
            vertices,
            BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.BindVertexArray(0);
    }

    public void Tick()
    {
        mCloudOffsetX += 1.0f;
    }

    public void ResetOffset()
    {
        mCloudOffsetX = 0.0f;
    }

    public void Render(Vector3 playerPos, WorldGenSettings settings, float dayFactor, float partialTick, Vector3 fogColor, float fogDist, Matrix4 view, Matrix4 proj)
    {
        float uvScrollU = (mCloudOffsetX + partialTick) * SCROLL_SPEED;

        float brightRG = dayFactor * .9f + .1f;
        float brightB = dayFactor * .85f + .15f;

        Vector3 modColor = new Vector3(
            settings.CloudColor.X * brightRG,
            settings.CloudColor.Y * brightRG,
            settings.CloudColor.Z * brightB
        );

        if (settings.Theme == WorldTheme.Paradise)
            modColor = settings.CloudColor;

        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);

        mShader.Use();
        mShader.SetMatrix4("view", view);
        mShader.SetMatrix4("projection", proj);
        mShader.SetVector3("playerPos", playerPos);
        mShader.SetFloat("cloudPlaneY", settings.CloudHeight);
        mShader.SetFloat("uvScrollU", uvScrollU);
        mShader.SetVector3("cloudColor", modColor);
        mShader.SetInt("cloudTexture", 0);

        mShader.SetVector3("fogColor", fogColor);
        mShader.SetFloat("fogStart", fogDist * 0.4f);
        mShader.SetFloat("fogEnd", fogDist * 0.9f);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, mCloudTexture.Handle);

        GL.BindVertexArray(mVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 12); // 12 verts = 2 faces × 2 tris × 3 verts
        GL.BindVertexArray(0);

        // Restore GL State
        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(mVAO);
        GL.DeleteBuffer(mVBO);
        mShader.Dispose();
        mCloudTexture.Dispose();
    }
}
