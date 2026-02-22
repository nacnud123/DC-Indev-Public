// Main class for the skybox. It renders and sets up the sun, moon, and stars. It holds the references to the shaders, textures, and Vao/Vbo for the respective sky elements. Also, has functions for moving the sky. | DA | 2/21/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VoxelEngine.Terrain;

namespace VoxelEngine.Rendering;

public class SkyRenderer : IDisposable
{
    const float SKY_DOME_Y = Chunk.HEIGHT + 10f;
    const float SKY_DOME_RADIUS = 2048f;
    const float SUN_HALF_SIZE = 30f;
    const float MOON_HALF_SIZE = 20f;
    const float CELESTIAL_DIST = 100f;
    const int STAR_COUNT = 500;
    const long STAR_SEED = 10842L;

    private Shader mSkyShader;
    private Shader mCelestialShader;
    private Shader mStarShader;
    private int mSkyVao, mSkyVbo;
    private int mSunVao, mSunVbo;
    private int mMoonVao, mMoonVbo;
    private int mStarVao, mStarVbo;
    private int mStarVertexCount = STAR_COUNT * 6;
    private Texture mSunTex;
    private Texture mMoonTex;

    public void Init()
    {
        mSkyShader = new Shader(File.ReadAllText("Shaders/Skybox/SkyVert.glsl"), File.ReadAllText("Shaders/Skybox/SkyFrag.glsl"));

        mCelestialShader = new Shader(File.ReadAllText("Shaders/Skybox/CelestialVert.glsl"), File.ReadAllText("Shaders/Skybox/CelestialFrag.glsl"));

        mStarShader = new Shader(File.ReadAllText("Shaders/Skybox/StarVert.glsl"), File.ReadAllText("Shaders/Skybox/StarFrag.glsl"));

        mSunTex = Texture.LoadFromFile("Resources/sun.png");
        mMoonTex = Texture.LoadFromFile("Resources/moon.png");

        BuildSkyDome();
        BuildSunQuad();
        BuildMoonQuad();
        BuildStars();
    }

    private void BuildSkyDome()
    {
        float[] vertices =
        {
            // Triangle 1
            -SKY_DOME_RADIUS, SKY_DOME_Y, -SKY_DOME_RADIUS,
            -SKY_DOME_RADIUS, SKY_DOME_Y, SKY_DOME_RADIUS,
            SKY_DOME_RADIUS, SKY_DOME_Y, SKY_DOME_RADIUS,
            // Triangle 2
            SKY_DOME_RADIUS, SKY_DOME_Y, SKY_DOME_RADIUS,
            SKY_DOME_RADIUS, SKY_DOME_Y, -SKY_DOME_RADIUS,
            -SKY_DOME_RADIUS, SKY_DOME_Y, -SKY_DOME_RADIUS
        };

        mSkyVao = GL.GenVertexArray();
        mSkyVbo = GL.GenBuffer();

        GL.BindVertexArray(mSkyVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mSkyVbo);
        GL.BufferData(BufferTarget.ArrayBuffer,
            vertices.Length * sizeof(float),
            vertices,
            BufferUsageHint.StaticDraw);
        // Atrib 0: pos, stride, offset
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.BindVertexArray(0);
    }

    private void BuildSunQuad()
    {
        float[] vertices =
        {
            // pos (x,      y,              z)           tex (u,   v)
            -SUN_HALF_SIZE, CELESTIAL_DIST, +SUN_HALF_SIZE, 0.0f, 0.0f, // top-left
            +SUN_HALF_SIZE, CELESTIAL_DIST, +SUN_HALF_SIZE, 1.0f, 0.0f, // top-right
            +SUN_HALF_SIZE, CELESTIAL_DIST, -SUN_HALF_SIZE, 1.0f, 1.0f, // bottom-right
            +SUN_HALF_SIZE, CELESTIAL_DIST, -SUN_HALF_SIZE, 1.0f, 1.0f, // bottom-right (repeated)
            -SUN_HALF_SIZE, CELESTIAL_DIST, -SUN_HALF_SIZE, 0.0f, 1.0f, // bottom-left
            -SUN_HALF_SIZE, CELESTIAL_DIST, +SUN_HALF_SIZE, 0.0f, 0.0f, // top-left (repeated)
        };

        mSunVao = GL.GenVertexArray();
        mSunVbo = GL.GenBuffer();
        GL.BindVertexArray(mSunVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mSunVbo);
        GL.BufferData(BufferTarget.ArrayBuffer,
            vertices.Length * sizeof(float),
            vertices,
            BufferUsageHint.StaticDraw);

        // Attrib 0: position, stride, offset
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false,
            5 * sizeof(float), 0);

        // Attrib 1: texCoord, stride, offset
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false,
            5 * sizeof(float), 3 * sizeof(float));
        GL.BindVertexArray(0);
    }

    private void BuildMoonQuad()
    {
        float[] vertices =
        {
            // pos (x,      y,       z)     tex (u,    v)   — U runs 1→0 (mirrored)
            -MOON_HALF_SIZE, -CELESTIAL_DIST, +MOON_HALF_SIZE, 1.0f, 1.0f, // top-left   (UV flipped)
            +MOON_HALF_SIZE, -CELESTIAL_DIST, +MOON_HALF_SIZE, 0.0f, 1.0f, // top-right
            +MOON_HALF_SIZE, -CELESTIAL_DIST, -MOON_HALF_SIZE, 0.0f, 0.0f, // bottom-right
            +MOON_HALF_SIZE, -CELESTIAL_DIST, -MOON_HALF_SIZE, 0.0f, 0.0f, // bottom-right (repeated)
            -MOON_HALF_SIZE, -CELESTIAL_DIST, -MOON_HALF_SIZE, 1.0f, 0.0f, // bottom-left
            -MOON_HALF_SIZE, -CELESTIAL_DIST, +MOON_HALF_SIZE, 1.0f, 1.0f, // top-left (repeated)
        };

        mMoonVao = GL.GenVertexArray();
        mMoonVbo = GL.GenBuffer();
        GL.BindVertexArray(mMoonVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mMoonVbo);
        GL.BufferData(BufferTarget.ArrayBuffer,
            vertices.Length * sizeof(float),
            vertices,
            BufferUsageHint.StaticDraw);

        // Attrib 0: position, stride, offset
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false,
            5 * sizeof(float), 0);

        // Attrib 1: texCoord, stride, offset
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false,
            5 * sizeof(float), 3 * sizeof(float));
        GL.BindVertexArray(0);
    }

    private void BuildStars()
    {
        var rng = new Random((int)STAR_SEED);

        float[] allVerts = new float[STAR_COUNT * 6 * 3];
        int idx = 0;

        for (int i = 0; i < STAR_COUNT; i++)
        {
            float rx = (float)rng.NextDouble() * 360f;
            float ry = (float)rng.NextDouble() * 360f;
            float rz = (float)rng.NextDouble() * 360f;
            float size = .25f + (float)rng.NextDouble() * .25f;

            Matrix4 rot = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(rx)) *
                          Matrix4.CreateRotationY(MathHelper.DegreesToRadians(ry)) *
                          Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rz));

            Vector3[] corners =
            {
                new Vector3(-size, -CELESTIAL_DIST, size),
                new Vector3(size, -CELESTIAL_DIST, size),
                new Vector3(size, -CELESTIAL_DIST, -size),
                new Vector3(-size, -CELESTIAL_DIST, -size),
            };

            for (int c = 0; c < 4; c++)
            {
                corners[c] = Vector3.TransformPosition(corners[c], rot);
            }

            // 2 triangles per quad
            int[] order = { 0, 1, 2, 0, 2, 3 };

            foreach (int ci in order)
            {
                allVerts[idx++] = corners[ci].X;
                allVerts[idx++] = corners[ci].Y;
                allVerts[idx++] = corners[ci].Z;
            }
        }

        mStarVao = GL.GenVertexArray();
        mStarVbo = GL.GenBuffer();
        GL.BindVertexArray(mStarVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mStarVbo);
        GL.BufferData(BufferTarget.ArrayBuffer,
            allVerts.Length * sizeof(float),
            allVerts,
            BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false,
            3 * sizeof(float), 0);
        GL.BindVertexArray(0);
        mStarVertexCount = STAR_COUNT * 6;
    }

    public void Render(Vector3 playerPos, float mTimeOfDay, WorldGenSettings settings, float dayFactor, Matrix4 view, Matrix4 proj)
    {
        float celestialAngle = mTimeOfDay - .25f;

        Vector3 skyColor = settings.DaySkyColor * dayFactor;

        float cosAngle = MathF.Cos(celestialAngle * MathF.PI * 2f);
        float t = Math.Clamp(1.0f - (cosAngle * 2.0f + .75f), 0f, 1f);
        float starBrightness = t * t * .5f;

        if (settings.Theme == WorldTheme.Paradise)
        {
            dayFactor = 1.0f;
            starBrightness = 0.0f;
        }

        // Celestial Matrix
        float angleDeg = celestialAngle * 360f;
        float angleRad = MathHelper.DegreesToRadians(angleDeg);

        Matrix4 celestialModel = Matrix4.CreateRotationX(angleRad) * Matrix4.CreateTranslation(playerPos);

        Matrix4 celestialMVP = celestialModel * view * proj;

        // GL Time
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace); // sky dome/sun/moon face toward camera from any angle

        // Sky Dome
        mSkyShader.Use();
        mSkyShader.SetMatrix4("view", view);
        mSkyShader.SetMatrix4("projection", proj);
        mSkyShader.SetVector3("playerPos", playerPos);
        mSkyShader.SetVector3("skyColor", skyColor);

        GL.BindVertexArray(mSkyVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);

        // SUN
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);

        mCelestialShader.Use();
        mCelestialShader.SetMatrix4("celestialMVP", celestialMVP);
        mCelestialShader.SetInt("celestialTex", 0);
        mCelestialShader.SetFloat("brightness", dayFactor); // sun dims at night

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, mSunTex.Handle);
        GL.BindVertexArray(mSunVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);

        // Moon
        mCelestialShader.SetFloat("brightness", 1.0f); // moon: texture defines brightness
        GL.BindTexture(TextureTarget.Texture2D, mMoonTex.Handle);
        GL.BindVertexArray(mMoonVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);

        // Stars
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        mStarShader.Use();
        mStarShader.SetMatrix4("celestialMVP", celestialMVP);
        mStarShader.SetFloat("starBrightness", starBrightness);

        GL.BindVertexArray(mStarVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mStarVertexCount);
        GL.BindVertexArray(0);

        // End GL time
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
        GL.DepthMask(true);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(mSkyVao);
        GL.DeleteBuffer(mSkyVbo);
        GL.DeleteVertexArray(mSunVao);
        GL.DeleteBuffer(mSunVbo);
        GL.DeleteVertexArray(mMoonVao);
        GL.DeleteBuffer(mMoonVbo);
        GL.DeleteVertexArray(mStarVao);
        GL.DeleteBuffer(mStarVbo);
        mSkyShader.Dispose();
        mCelestialShader.Dispose();
        mStarShader.Dispose();
        mSunTex.Dispose();
        mMoonTex.Dispose();
    }
}
