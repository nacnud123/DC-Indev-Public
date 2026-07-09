// Main class for the skybox. It renders and sets up the sun, moon, and stars. | DA | 2/21/26
using Silk.NET.OpenGL;

using VoxelEngine.Terrain;

namespace VoxelEngine.Rendering;

/// <summary>
/// Draws the sky: a large flat-shaded dome quad tinted by time-of-day color, a billboard sun and moon that rotate opposite each other around the player, and a field of procedurally scattered stars that fade in/out with day/night. All geometry is static (built once in Init); only shader uniforms and the celestial rotation matrix change per frame.
/// </summary>
public class SkyRenderer : IDisposable
{
    // SKY_DOME_Y sits just above the world's max build height so the dome never intersects terrain.
    const float SKY_DOME_Y = Chunk.HEIGHT + 10f;
    const float SKY_DOME_RADIUS = 2048f; // far enough out that it's effectively "at infinity" vs. render distance
    const float SUN_HALF_SIZE = 30f;
    const float MOON_HALF_SIZE = 20f;
    const float CELESTIAL_DIST = 100f; // distance of the sun/moon quads from the player along their orbit
    const int STAR_COUNT = 500;
    const long STAR_SEED = 10842L; // fixed seed so the star field is identical every run (not randomized per session)

    private Shader mSkyShader;
    private Shader mCelestialShader; // shared by both the sun and moon quads (same shading, different texture/brightness)
    private Shader mStarShader;
    private uint mSkyVao, mSkyVbo;
    private uint mSunVao, mSunVbo;
    private uint mMoonVao, mMoonVbo;
    private uint mStarVao, mStarVbo;
    private int mStarVertexCount = STAR_COUNT * 6; // 6 vertices (2 tris) per star quad
    private Texture mSunTex;
    private Texture mMoonTex;

    /// <summary>Loads sky/celestial/star shaders and textures, then builds all static GL geometry. Call once at startup.</summary>
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

    // Builds a single huge flat quad (not a true dome mesh) positioned above the world and centered on the origin; SkyVert/SkyFrag shaders recenter it on the player and tint it per-fragment based on view angle, so a flat quad reads visually as an enclosing sky.
    private void BuildSkyDome()
    {
        float[] vertices =
        {
            -SKY_DOME_RADIUS, SKY_DOME_Y, -SKY_DOME_RADIUS,
            -SKY_DOME_RADIUS, SKY_DOME_Y, SKY_DOME_RADIUS,
            SKY_DOME_RADIUS, SKY_DOME_Y, SKY_DOME_RADIUS,
            SKY_DOME_RADIUS, SKY_DOME_Y, SKY_DOME_RADIUS,
            SKY_DOME_RADIUS, SKY_DOME_Y, -SKY_DOME_RADIUS,
            -SKY_DOME_RADIUS, SKY_DOME_Y, -SKY_DOME_RADIUS
        };

        var gl = GlContext.Gl;
        mSkyVao = gl.GenVertexArray();
        mSkyVbo = gl.GenBuffer();

        gl.BindVertexArray(mSkyVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mSkyVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(3 * sizeof(float)), 0);
        gl.BindVertexArray(0);
    }

    // Sun quad is placed at +CELESTIAL_DIST on the Y axis; the whole quad is later rotated around the player each frame (see Render's celestialModel) to swing it across the sky over the day cycle.
    private void BuildSunQuad()
    {
        float[] vertices =
        {
            -SUN_HALF_SIZE, CELESTIAL_DIST, +SUN_HALF_SIZE, 0.0f, 0.0f,
            +SUN_HALF_SIZE, CELESTIAL_DIST, +SUN_HALF_SIZE, 1.0f, 0.0f,
            +SUN_HALF_SIZE, CELESTIAL_DIST, -SUN_HALF_SIZE, 1.0f, 1.0f,
            +SUN_HALF_SIZE, CELESTIAL_DIST, -SUN_HALF_SIZE, 1.0f, 1.0f,
            -SUN_HALF_SIZE, CELESTIAL_DIST, -SUN_HALF_SIZE, 0.0f, 1.0f,
            -SUN_HALF_SIZE, CELESTIAL_DIST, +SUN_HALF_SIZE, 0.0f, 0.0f,
        };

        var gl = GlContext.Gl;
        mSunVao = gl.GenVertexArray();
        mSunVbo = gl.GenBuffer();
        gl.BindVertexArray(mSunVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mSunVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(5 * sizeof(float)), 0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(5 * sizeof(float)), (nint)(3 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    // Moon quad is placed at -CELESTIAL_DIST (opposite the sun) so the same rotation matrix that swings the sun up during the day swings the moon up during the night automatically - they're rigidly 180 degrees apart on the same rotating axis, no separate moon-phase logic needed.
    private void BuildMoonQuad()
    {
        float[] vertices =
        {
            -MOON_HALF_SIZE, -CELESTIAL_DIST, +MOON_HALF_SIZE, 1.0f, 1.0f,
            +MOON_HALF_SIZE, -CELESTIAL_DIST, +MOON_HALF_SIZE, 0.0f, 1.0f,
            +MOON_HALF_SIZE, -CELESTIAL_DIST, -MOON_HALF_SIZE, 0.0f, 0.0f,
            +MOON_HALF_SIZE, -CELESTIAL_DIST, -MOON_HALF_SIZE, 0.0f, 0.0f,
            -MOON_HALF_SIZE, -CELESTIAL_DIST, -MOON_HALF_SIZE, 1.0f, 0.0f,
            -MOON_HALF_SIZE, -CELESTIAL_DIST, +MOON_HALF_SIZE, 1.0f, 1.0f,
        };

        var gl = GlContext.Gl;
        mMoonVao = gl.GenVertexArray();
        mMoonVbo = gl.GenBuffer();
        gl.BindVertexArray(mMoonVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mMoonVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(5 * sizeof(float)), 0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(5 * sizeof(float)), (nint)(3 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    // Procedurally scatters STAR_COUNT small quads onto an imaginary sphere around the player. Each star starts as a flat quad on the "south pole" (-CELESTIAL_DIST on Y) and is then rotated by a random XYZ Euler rotation, which is a cheap way to distribute points roughly uniformly over a sphere's surface without computing spherical coordinates directly. STAR_SEED is fixed so the same star pattern is generated every run instead of jittering between sessions.
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

            // Combined rotation applied to this star's quad to place it at a pseudo-random point on the celestial sphere; order (X then Y then Z) matches Matrix4x4's row-vector multiply convention.
            Matrix4x4 rot = Matrix4x4.CreateRotationX(float.DegreesToRadians(rx)) *
                          Matrix4x4.CreateRotationY(float.DegreesToRadians(ry)) *
                          Matrix4x4.CreateRotationZ(float.DegreesToRadians(rz));

            // Quad built flat at the "bottom" of the sphere before rotation is applied below.
            Vector3[] corners =
            {
                new Vector3(-size, -CELESTIAL_DIST, size),
                new Vector3(size, -CELESTIAL_DIST, size),
                new Vector3(size, -CELESTIAL_DIST, -size),
                new Vector3(-size, -CELESTIAL_DIST, -size),
            };

            for (int c = 0; c < 4; c++)
                corners[c] = Vector3.Transform(corners[c], rot);

            // Two triangles per quad: (0,1,2) and (0,2,3).
            int[] order = { 0, 1, 2, 0, 2, 3 };
            foreach (int ci in order)
            {
                allVerts[idx++] = corners[ci].X;
                allVerts[idx++] = corners[ci].Y;
                allVerts[idx++] = corners[ci].Z;
            }
        }

        var gl = GlContext.Gl;
        mStarVao = gl.GenVertexArray();
        mStarVbo = gl.GenBuffer();
        gl.BindVertexArray(mStarVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mStarVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, allVerts, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)(3 * sizeof(float)), 0);
        gl.BindVertexArray(0);
        mStarVertexCount = STAR_COUNT * 6;
    }

    /// <summary>
    /// Draws the sky dome, sun, moon, and stars for the current frame. Must run before opaque terrain (it disables depth writes) so the sky never occludes real geometry but still respects depth testing against anything already in the depth buffer from a previous pass.
    /// </summary>
    public void Render(Vector3 playerPos, float mTimeOfDay, WorldGenSettings settings, float dayFactor, Matrix4x4 view, Matrix4x4 proj)
    {
        // Time-of-day is [0,1) with 0=dawn, 0.25=noon, 0.5=dusk, 0.75=midnight (per project convention). Subtracting 0.25 realigns the cycle so the sun is directly overhead (angle=0) at noon instead of at dawn, matching how the rotation is applied to the celestial quads below.
        float celestialAngle = mTimeOfDay - .25f;

        Vector3 skyColor = settings.DaySkyColor * dayFactor;

        // Star brightness follows a cosine curve of the celestial angle so stars fade in around dusk, peak at midnight, and fade out around dawn. The `t * t` squaring makes the fade sharper near full daylight (stars snap off quickly) rather than a slow linear fade that would be visible at noon.
        float cosAngle = MathF.Cos(celestialAngle * MathF.PI * 2f);
        float t = Math.Clamp(1.0f - (cosAngle * 2.0f + .75f), 0f, 1f);
        float starBrightness = t * t * .5f;

        // Paradise-themed worlds have no night cycle: force full daylight and no stars regardless of time.
        if (settings.Theme == WorldTheme.Paradise)
        {
            dayFactor = 1.0f;
            starBrightness = 0.0f;
        }

        float angleDeg = celestialAngle * 360f;
        float angleRad = float.DegreesToRadians(angleDeg);

        // Rotate the sun/moon/star geometry around the X axis (an east-west arc) and then translate to the player's position, since all celestial geometry was built centered on the world origin. Order matters: rotate first (around the origin) then translate, otherwise the orbit would be centered on the player instead of sweeping overhead.
        Matrix4x4 celestialModel = Matrix4x4.CreateRotationX(angleRad) * Matrix4x4.CreateTranslation(playerPos);
        Matrix4x4 celestialMVP = celestialModel * view * proj;

        var gl = GlContext.Gl;
        // Depth writes disabled for the whole sky pass: sky/sun/moon/stars should never occlude anything drawn afterward (terrain, entities), but they still depth-test against prior passes so nothing draws through solid geometry that's already in front of the camera.
        gl.DepthMask(false);
        gl.Disable(EnableCap.CullFace);

        // Sky Dome
        mSkyShader.Use();
        mSkyShader.SetMatrix4("view", view);
        mSkyShader.SetMatrix4("projection", proj);
        mSkyShader.SetVector3("playerPos", playerPos);
        mSkyShader.SetVector3("skyColor", skyColor);

        gl.BindVertexArray(mSkyVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.BindVertexArray(0);

        // SUN
        gl.Enable(EnableCap.Blend);
        // Additive blending (One, One) for the sun so its bright core doesn't get dimmed by the sky color behind it - light adds onto the framebuffer rather than alpha-compositing over it.
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);

        mCelestialShader.Use();
        mCelestialShader.SetMatrix4("celestialMVP", celestialMVP);
        mCelestialShader.SetInt("celestialTex", 0);
        mCelestialShader.SetFloat("brightness", dayFactor);

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, mSunTex.Handle);
        gl.BindVertexArray(mSunVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.BindVertexArray(0);

        // Moon Moon is always drawn at full brightness (1.0) regardless of dayFactor - the moon texture's own alpha/shape defines visibility, unlike the sun whose apparent brightness fades with dayFactor.
        mCelestialShader.SetFloat("brightness", 1.0f);
        gl.BindTexture(TextureTarget.Texture2D, mMoonTex.Handle);
        gl.BindVertexArray(mMoonVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.BindVertexArray(0);

        // Stars use standard alpha blending (not additive like the sun) so overlapping star quads don't wash out to solid white.
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        mStarShader.Use();
        mStarShader.SetMatrix4("celestialMVP", celestialMVP);
        mStarShader.SetFloat("starBrightness", starBrightness);

        gl.BindVertexArray(mStarVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mStarVertexCount);
        gl.BindVertexArray(0);

        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.CullFace);
        gl.DepthMask(true);
    }

    /// <summary>Releases all GL objects (VAOs/VBOs/shaders/textures) owned by this renderer.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mSkyVao);
        gl.DeleteBuffer(mSkyVbo);
        gl.DeleteVertexArray(mSunVao);
        gl.DeleteBuffer(mSunVbo);
        gl.DeleteVertexArray(mMoonVao);
        gl.DeleteBuffer(mMoonVbo);
        gl.DeleteVertexArray(mStarVao);
        gl.DeleteBuffer(mStarVbo);
        mSkyShader.Dispose();
        mCelestialShader.Dispose();
        mStarShader.Dispose();
        mSunTex.Dispose();
        mMoonTex.Dispose();
    }
}
