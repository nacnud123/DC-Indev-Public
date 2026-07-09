// Owns and orchestrates all per-frame OpenGL rendering, extracted from Game.cs | DA | 2026
using System;
using System.Linq;
using ImGuiNET;
using Silk.NET.OpenGL;

using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Particles;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.UI;
using VoxelEngine.Utils;

namespace VoxelEngine.Rendering;

/// <summary>
/// Owns and orchestrates every per-frame OpenGL draw call for an active play session. This is the single place that defines render order (see <see cref="RenderFrame"/>): sky -> clouds -> world (opaque) -> world (transparent) -> blob shadows -> entities -> paintings -> particles -> block highlight -> player arm -> HUD (crosshair, then ImGui overlay separately via <see cref="RenderHudOverlay"/>). Sub-renderers for each stage (sky, clouds, shadows, etc.) are created once in <see cref="Init"/> and reused every frame; per-world/per-player state (world, player, hotbar, etc.) is injected via <see cref="SetSession"/> when a game session starts and cleared via <see cref="ClearSession"/> on teardown, so this class can outlive individual play sessions (e.g. across returning to the main menu and loading a new world).
/// </summary>
public class GameRenderer : IDisposable
{
    // OpenGL rendering objects (owned by this class)
    private SkyRenderer mSkyRenderer = null!;
    private CloudRenderer mCloudRenderer = null!;
    private BlobShadowRenderer mBlobShadowRenderer = null!;
    private Crosshair mCrosshair = null!;
    private BlockHighlight mBlockHighlight = null!;
    private BlockBreakOverlay mBlockBreakOverlay = null!;
    private Texture mBreakTexture = null!;
    private PaintingRenderer mPaintingRenderer = null!;

    // Shared resources (not owned - lifetime managed by Game)
    private Shader mWorldShader = null!;
    private Texture mWorldTexture = null!;
    private Texture mPaintingsTexture = null!;
    private ParticleSystem mParticleSystem = null!;

    // Per-session references (set after world load, cleared on teardown)
    private World? mWorld;
    private Player? mPlayer;
    private TickSystem? mTickSystem;
    private PlayerArm? mPlayerArm;
    private Hotbar? mHotbar;
    private HudScreen? mHudScreen;

    private bool mDisposed;

    /// <summary>
    /// One-time setup: stores shared shader/texture/particle references owned by <c>Game</c>, and creates + initializes all the per-stage sub-renderers used by <see cref="RenderFrame"/>. Must be called once before any world session starts.
    /// </summary>
    public void Init(Shader worldShader, Texture worldTexture, Texture paintingsTexture, ParticleSystem particles)
    {
        mWorldShader = worldShader;
        mWorldTexture = worldTexture;
        mPaintingsTexture = paintingsTexture;
        mParticleSystem = particles;

        mSkyRenderer = new SkyRenderer();
        mSkyRenderer.Init();
        mCloudRenderer = new CloudRenderer();
        mCloudRenderer.Init();
        mBlobShadowRenderer = new BlobShadowRenderer();
        mCrosshair = new Crosshair();
        mBlockHighlight = new BlockHighlight();
        mBlockBreakOverlay = new BlockBreakOverlay();
        mBreakTexture = Texture.LoadFromFile("Resources/break.png");
        mPaintingRenderer = new PaintingRenderer();
        mPaintingRenderer.Init();
    }

    /// <summary>Binds this renderer to an active world/player session. Called once a world finishes loading.</summary>
    internal void SetSession(World world, Player player, TickSystem tickSystem, PlayerArm? arm, Hotbar hotbar, HudScreen hud)
    {
        mWorld = world;
        mPlayer = player;
        mTickSystem = tickSystem;
        mPlayerArm = arm;
        mHotbar = hotbar;
        mHudScreen = hud;
    }

    /// <summary>Detaches the current session references (world unloaded / returning to menu). Makes RenderFrame a no-op.</summary>
    public void ClearSession()
    {
        mWorld = null;
        mPlayer = null;
        mTickSystem = null;
        mPlayerArm = null;
        mHotbar = null;
        mHudScreen = null;
    }

    /// <summary>Advances cloud scroll animation by one fixed game tick; forwarded to the tick system.</summary>
    public void TickClouds() => mCloudRenderer?.Tick();
    /// <summary>Resets cloud scroll animation (e.g. on world load) so clouds don't jump.</summary>
    public void ResetCloudOffset() => mCloudRenderer?.ResetOffset();

    /// <summary>
    /// Draws one full frame of the 3D world for the current session, in the fixed pipeline order: sky, clouds, opaque world geometry, transparent world geometry, blob shadows, entities, paintings, particles, block highlight/break overlay, first-person player arm, and finally the crosshair. View/projection matrices are computed once here from the player's camera and threaded through every stage so they all agree on the same frame's camera state. No-ops if there is no active session (world/player not set).
    /// </summary>
    public void RenderFrame(float timeOfDay, WorldGenSettings settings)
    {
        if (mWorld == null || mPlayer == null) return;

        var view = mPlayer.Camera.GetViewMatrix();
        var proj = mPlayer.Camera.GetProjectionMatrix();

        RenderSky(view, proj, timeOfDay, settings);
        RenderClouds(view, proj, timeOfDay, settings);
        RenderWorld(view, proj, timeOfDay, settings);
        RenderShadows(view, proj);
        RenderEntities(view, proj);
        RenderPaintings(view, proj);
        RenderParticles(view, proj);
        RenderBlockHighlight(view, proj);
        mPlayerArm?.Render(mPlayer.Camera, mHotbar?.GetSelectedStack());
        RenderHud();
    }

    /// <summary>
    /// Draws the ImGui-based HUD (hotbar, health, etc.) plus the full-screen fire vignette when the player is on fire. Separate from <see cref="RenderFrame"/> because ImGui draw calls happen in a different pass (ImGui's own render loop) than the raw GL calls in the 3D frame.
    /// </summary>
    public void RenderHudOverlay()
    {
        if (mHudScreen == null || mHotbar == null || mPlayer == null) return;
        var display = ImGui.GetIO().DisplaySize;
        mHudScreen.Render(
            mHotbar.GetHotbarX(display.X),
            mHotbar.GetHotbarY(display.Y),
            mHotbar.HotbarWidth);

        if (mPlayer.IsOnFire)
            RenderFireOverlay(display);
    }

    // Draws the sky dome/background. Recomputes dayFactor (0=full night, 1=full day) from timeOfDay via a clamped sine curve so lighting transitions ease in/out near dawn/dusk rather than changing linearly.
    private void RenderSky(Matrix4x4 view, Matrix4x4 proj, float timeOfDay, WorldGenSettings settings)
    {
        // timeOfDay in [0,1) maps to a full 0..2π sine cycle; sin() peaks at noon (0.25) and troughs at midnight (0.75). Clamped to a 0.05 floor so it's never fully black (some ambient light always remains) and multiplied by 2 to reach full brightness before noon.
        float sunAngle = timeOfDay * MathF.PI * 2f;
        float sunLightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, .05f, 1.0f);
        // Remap sunLightLevel's [0.05, 1.0] range to a normalized [0, 1] dayFactor for color lerps.
        float dayFactor = (sunLightLevel - 0.05f) / 0.95f;

        mSkyRenderer.Render(mPlayer!.Position, timeOfDay, settings, dayFactor, view, proj);
    }

    // Draws the scrolling cloud plane. Uses the tick system's partial-tick value to interpolate cloud scroll smoothly between fixed ticks (avoids visible stepping when frame rate > tick rate).
    private void RenderClouds(Matrix4x4 view, Matrix4x4 proj, float timeOfDay, WorldGenSettings settings)
    {
        float sunAngle = timeOfDay * MathF.PI * 2f;
        float sunLightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, .05f, 1.0f);
        float dayFactor = (sunLightLevel - .05f) / .95f;

        float partialTick = mTickSystem!.GetPartialTick();
        float fogDist = mPlayer!.Camera.RenderDistance;

        // Fog color blends from a very dark ("night sky") tint up to the configured day fog color.
        Vector3 nightSky = settings.DaySkyColor * 0.02f;
        Vector3 fogColor = Vector3.Lerp(nightSky, settings.DayFogColor, dayFactor);

        mCloudRenderer.Render(mPlayer.Position, settings, dayFactor, partialTick, fogColor, fogDist, view, proj);
    }

    // Draws all opaque chunk geometry, then all transparent chunk geometry (water/glass) in a second pass with blending enabled. Also sets the shared lighting/fog uniforms used by entities later in the frame (via the static Entity.LightDir/AmbientStrength/SunlightLevel fields) and picks fog/clear color based on whether the camera is submerged in water or lava.
    private void RenderWorld(Matrix4x4 view, Matrix4x4 proj, float timeOfDay, WorldGenSettings settings)
    {
        float sunAngle = timeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0.05f, 1.0f);
        float dayFactor = (sunlightLevel - 0.05f) / 0.95f;

        // Directional "sun" vector: rotates around the horizon following sunAngle, with a fixed downward -0.3 Z bias so the sun is never perfectly horizontal (keeps shading from going degenerate at dawn/dusk). Normalized because it's used as a light direction.
        Vector3 lightDir = Vector3.Normalize(new Vector3(-MathF.Cos(sunAngle), -MathF.Sin(sunAngle), -0.3f));
        Vector3 lightColor = GetSunColor(dayFactor);
        Vector3 nightSky = settings.DaySkyColor * 0.02f;
        Vector3 skyColor = Vector3.Lerp(nightSky, settings.DaySkyColor, dayFactor);
        Vector3 fogColor = Vector3.Lerp(nightSky, settings.DayFogColor, dayFactor);
        float ambientStrength = 0.08f + dayFactor * 0.22f;

        // Published as static state so Entity/mob rendering later in the frame (RenderEntities) picks up the same lighting without needing these values passed through explicitly.
        Entity.LightDir = lightDir;
        Entity.AmbientStrength = ambientStrength + 0.1f;
        Entity.SunlightLevel = sunlightLevel;

        float fogDist = mPlayer!.Camera.RenderDistance;

        mWorldShader.Use();
        mWorldShader.SetMatrix4("model", Matrix4x4.Identity);
        mWorldShader.SetMatrix4("view", view);
        mWorldShader.SetMatrix4("projection", proj);
        mWorldShader.SetVector3("lightDir", lightDir);
        mWorldShader.SetVector3("lightColor", lightColor);
        mWorldShader.SetFloat("ambientStrength", ambientStrength);
        mWorldShader.SetFloat("sunlightLevel", sunlightLevel);
        mWorldShader.SetVector3("fogColor", fogColor);

        var gl = GlContext.Gl;
        // Underwater/underlava fog overrides completely replace the normal day/night fog: a much shorter, tinted fog band so the player can't see far, plus a matching clear color so the "void" beyond the far fog plane matches the submerged tint instead of the sky.
        if (mPlayer.IsUnderWater)
        {
            mWorldShader.SetFloat("fogStart", 2.0f);
            mWorldShader.SetFloat("fogEnd", fogDist - 5f);
            mWorldShader.SetVector3("fogColor", new Vector3(.05f, .1f, .3f));
            gl.ClearColor(.05f, .1f, .3f, 1.0f);
        }
        else if (mPlayer.IsUnderLava)
        {
            mWorldShader.SetFloat("fogStart", 0.5f);
            mWorldShader.SetFloat("fogEnd", 3.0f);
            mWorldShader.SetVector3("fogColor", new Vector3(.4f, .1f, .05f));
            gl.ClearColor(.4f, .1f, .05f, 1.0f);
        }
        else
        {
            mWorldShader.SetFloat("fogStart", fogDist * 0.4f);
            mWorldShader.SetFloat("fogEnd", fogDist * 0.9f);
            mWorldShader.SetVector3("fogColor", fogColor);
            gl.ClearColor(skyColor.X, skyColor.Y, skyColor.Z, 1.0f);
        }

        // Tells the shader which fluid tint/distortion to apply to the screen: 0=none, 1=water, 2=lava.
        int fluidType = mPlayer.IsUnderWater ? 1 : mPlayer.IsUnderLava ? 2 : 0;
        mWorldShader.SetInt("fluidType", fluidType);

        mWorldTexture.Use(TextureUnit.Texture0);
        mWorldShader.SetInt("blockTexture", 0);

        // Pass 1: opaque geometry. alphaOverride=0 tells the shader to use each block's native alpha (effectively opaque) rather than a forced blend value.
        mWorldShader.SetFloat("alphaOverride", 0.0f);
        mWorld!.Render(mPlayer.Camera);

        // Pass 2: transparent geometry (water, glass, etc). Standard alpha blending is enabled; depth writes are disabled so overlapping transparent faces don't occlude each other based on draw order, while depth testing against the opaque pass remains active. Backface culling is disabled so the far side of transparent volumes (e.g. water) still renders. A small negative polygon offset pushes transparent faces slightly toward the camera to avoid z-fighting with coincident opaque faces (e.g. water surface flush with a shoreline block).
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(-1f, -1f);
        gl.DepthMask(false);
        gl.Disable(EnableCap.CullFace);

        mWorldShader.SetFloat("alphaOverride", 0.7f);
        mWorld.RenderTransparent(mPlayer.Camera);

        // Restore GL state back to the defaults the rest of the frame (and other renderers) expect.
        gl.Enable(EnableCap.CullFace);
        gl.DepthMask(true);
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.Disable(EnableCap.Blend);
        mWorldShader.SetFloat("alphaOverride", 0.0f);
    }

    // Interpolates the directional light (sun/moon) color across the day cycle: night(0.3,0.3,0.5) -> sunset(1,0.6,0.3) -> day(1,1,0.95)
    private static Vector3 GetSunColor(float dayFactor)
    {
        if (dayFactor > 0.5f)
            return Vector3.Lerp(new Vector3(1f, 0.6f, 0.3f), new Vector3(1f, 1f, 0.95f), (dayFactor - 0.5f) * 2f);
        return Vector3.Lerp(new Vector3(0.3f, 0.3f, 0.5f), new Vector3(1f, 0.6f, 0.3f), dayFactor * 2f);
    }

    // Draws soft blob shadows under every entity (including the player). Entities and the player share one draw call by concatenating them via Append rather than special-casing the player.
    private void RenderShadows(Matrix4x4 view, Matrix4x4 proj)
    {
        var allEntities = mWorld!.Entities.Append(mPlayer!);
        mBlobShadowRenderer.Render(allEntities, mWorld, view, proj, mPlayer.Camera.Position);
    }

    // Draws all mobs/entities in render distance. Publishes the camera position via the static Entity.CameraPosition field, consumed by entity shaders for billboarding/distance fade etc.
    private void RenderEntities(Matrix4x4 view, Matrix4x4 proj)
    {
        Entity.CameraPosition = mPlayer!.Camera.Position;
        mWorld!.RenderEntities(view, proj, mPlayer.Camera.Position, mPlayer.Camera.RenderDistance);
    }

    // Draws in-world painting entities using their own texture atlas, separate from the block/item atlases.
    private void RenderPaintings(Matrix4x4 view, Matrix4x4 proj)
    {
        var paintings = mWorld!.Entities.OfType<PaintingEntity>();
        mPaintingRenderer.Render(paintings, mPaintingsTexture, view, proj);
    }

    // Draws block-texture particles (dust, breaking-block bits) and smoke particles. Depth writes are disabled for the whole particle pass so overlapping particles don't fight each other or punch holes in geometry behind them, while still depth-testing against solid world geometry.
    private void RenderParticles(Matrix4x4 view, Matrix4x4 proj)
    {
        GlContext.Gl.DepthMask(false);
        mParticleSystem.Render(view, proj, mWorldTexture);
        mParticleSystem.RenderSmoke(view, proj);
        GlContext.Gl.DepthMask(true);
    }

    // Draws the wireframe outline around the block the player is looking at, plus the progressive break-stage crack overlay while a block is being mined.
    private void RenderBlockHighlight(Matrix4x4 view, Matrix4x4 proj)
    {
        var hit = mWorld!.Raycast(mPlayer!.Camera.Position, mPlayer.Camera.Front);

        if (hit.Type == RaycastHitType.Block)
        {
            var boundsMin = BlockRegistry.GetBoundsMin(hit.BlockType);
            var boundsMax = BlockRegistry.GetBoundsMax(hit.BlockType);

            // Torches use per-metadata wall-mounted bounding boxes instead of the registry default (metadata 0 = standing torch, uses default bounds; >0 = wall torch facing direction).
            if (hit.BlockType == BlockType.Torch)
            {
                int meta = mWorld.GetMetadata(hit.BlockPos.X, hit.BlockPos.Y, hit.BlockPos.Z);
                if (meta > 0)
                    (boundsMin, boundsMax) = BlockTorch.GetWallTorchBounds(meta - 1);
            }

            mBlockHighlight.Render(hit.BlockPos, view, proj, boundsMin, boundsMax);
        }

        // Break stage < 0 means the player isn't currently mining anything.
        int breakStage = mPlayer.GetBreakStage();
        if (breakStage >= 0 && mPlayer.BreakingBlockPos.HasValue)
            mBlockBreakOverlay.Render(mPlayer.BreakingBlockPos.Value, breakStage, view, proj, mBreakTexture);
    }

    // Draws the center-screen crosshair on top of everything else in the 3D scene by disabling depth testing for this one draw, then restoring it immediately after.
    private void RenderHud()
    {
        GlContext.Gl.Disable(EnableCap.DepthTest);
        mCrosshair.Render();
        GlContext.Gl.Enable(EnableCap.DepthTest);
    }

    // Draws a full-screen reddish fire vignette (using a tile from the world atlas as a full-screen tinted overlay) when the player is burning, via ImGui's background draw list rather than a raw GL draw so it composites correctly with the rest of the ImGui HUD pass.
    private void RenderFireOverlay(System.Numerics.Vector2 display)
    {
        var fireUv = UvHelper.FromTileCoords(6, 7);
        // ImGui's AddImage expects UV0 = top-left and UV1 = bottom-right in image space (Y down), while UvHelper's TopLeft/BottomRight are in GL texture space (Y up) - so the Y components are swapped here to flip the sampled region vertically for correct on-screen orientation.
        var uvMin = new System.Numerics.Vector2(fireUv.TopLeft.X, fireUv.BottomRight.Y);
        var uvMax = new System.Numerics.Vector2(fireUv.BottomRight.X, fireUv.TopLeft.Y);
        // ARGB-ish packed tint (0x990066FF): translucent reddish-orange overlay color.
        uint tint = 0x990066FF;

        ImGui.GetBackgroundDrawList().AddImage(
            new IntPtr(mWorldTexture.Handle),
            System.Numerics.Vector2.Zero,
            display,
            uvMin, uvMax,
            tint);
    }

    /// <summary>Releases all GL resources owned by the per-stage sub-renderers. Safe to call multiple times.</summary>
    public void Dispose()
    {
        if (mDisposed) return;
        mDisposed = true;

        mSkyRenderer?.Dispose();
        mCloudRenderer?.Dispose();
        mBlobShadowRenderer?.Dispose();
        mCrosshair?.Dispose();
        mBlockHighlight?.Dispose();
        mBlockBreakOverlay?.Dispose();
        mBreakTexture?.Dispose();
        mPaintingRenderer?.Dispose();
    }
}
