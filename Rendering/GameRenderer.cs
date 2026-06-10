// Owns and orchestrates all per-frame OpenGL rendering, extracted from Game.cs | DA | 2026
using System;
using System.Linq;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Particles;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.UI;
using VoxelEngine.Utils;

namespace VoxelEngine.Rendering;

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

    // Shared resources (not owned — lifetime managed by Game)
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

    internal void SetSession(World world, Player player, TickSystem tickSystem, PlayerArm? arm, Hotbar hotbar, HudScreen hud)
    {
        mWorld = world;
        mPlayer = player;
        mTickSystem = tickSystem;
        mPlayerArm = arm;
        mHotbar = hotbar;
        mHudScreen = hud;
    }

    public void ClearSession()
    {
        mWorld = null;
        mPlayer = null;
        mTickSystem = null;
        mPlayerArm = null;
        mHotbar = null;
        mHudScreen = null;
    }

    public void TickClouds() => mCloudRenderer?.Tick();
    public void ResetCloudOffset() => mCloudRenderer?.ResetOffset();

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

    private void RenderSky(Matrix4 view, Matrix4 proj, float timeOfDay, WorldGenSettings settings)
    {
        float sunAngle = timeOfDay * MathF.PI * 2f;
        float sunLightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, .05f, 1.0f);
        float dayFactor = (sunLightLevel - 0.05f) / 0.95f;

        mSkyRenderer.Render(mPlayer!.Position, timeOfDay, settings, dayFactor, view, proj);
    }

    private void RenderClouds(Matrix4 view, Matrix4 proj, float timeOfDay, WorldGenSettings settings)
    {
        float sunAngle = timeOfDay * MathF.PI * 2f;
        float sunLightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, .05f, 1.0f);
        float dayFactor = (sunLightLevel - .05f) / .95f;

        float partialTick = mTickSystem!.GetPartialTick();
        float fogDist = mPlayer!.Camera.RenderDistance;

        Vector3 nightSky = settings.DaySkyColor * 0.02f;
        Vector3 fogColor = Vector3.Lerp(nightSky, settings.DayFogColor, dayFactor);

        mCloudRenderer.Render(mPlayer.Position, settings, dayFactor, partialTick, fogColor, fogDist, view, proj);
    }

    private void RenderWorld(Matrix4 view, Matrix4 proj, float timeOfDay, WorldGenSettings settings)
    {
        float sunAngle = timeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0.05f, 1.0f);
        float dayFactor = (sunlightLevel - 0.05f) / 0.95f;

        Vector3 lightDir = new Vector3(-MathF.Cos(sunAngle), -MathF.Sin(sunAngle), -0.3f).Normalized();
        Vector3 lightColor = GetSunColor(dayFactor);
        Vector3 nightSky = settings.DaySkyColor * 0.02f;
        Vector3 skyColor = Vector3.Lerp(nightSky, settings.DaySkyColor, dayFactor);
        Vector3 fogColor = Vector3.Lerp(nightSky, settings.DayFogColor, dayFactor);
        float ambientStrength = 0.08f + dayFactor * 0.22f;

        Entity.LightDir = lightDir;
        Entity.AmbientStrength = ambientStrength + 0.1f;
        Entity.SunlightLevel = sunlightLevel;

        float fogDist = mPlayer!.Camera.RenderDistance;

        mWorldShader.Use();
        mWorldShader.SetMatrix4("model", Matrix4.Identity);
        mWorldShader.SetMatrix4("view", view);
        mWorldShader.SetMatrix4("projection", proj);
        mWorldShader.SetVector3("lightDir", lightDir);
        mWorldShader.SetVector3("lightColor", lightColor);
        mWorldShader.SetFloat("ambientStrength", ambientStrength);
        mWorldShader.SetFloat("sunlightLevel", sunlightLevel);
        mWorldShader.SetVector3("fogColor", fogColor);

        if (mPlayer.IsUnderWater)
        {
            mWorldShader.SetFloat("fogStart", 2.0f);
            mWorldShader.SetFloat("fogEnd", fogDist - 5f);
            mWorldShader.SetVector3("fogColor", new Vector3(.05f, .1f, .3f));
            GL.ClearColor(.05f, .1f, .3f, 1.0f);
        }
        else if (mPlayer.IsUnderLava)
        {
            mWorldShader.SetFloat("fogStart", 0.5f);
            mWorldShader.SetFloat("fogEnd", 3.0f);
            mWorldShader.SetVector3("fogColor", new Vector3(.4f, .1f, .05f));
            GL.ClearColor(.4f, .1f, .05f, 1.0f);
        }
        else
        {
            mWorldShader.SetFloat("fogStart", fogDist * 0.4f);
            mWorldShader.SetFloat("fogEnd", fogDist * 0.9f);
            mWorldShader.SetVector3("fogColor", fogColor);
            GL.ClearColor(skyColor.X, skyColor.Y, skyColor.Z, 1.0f);
        }

        int fluidType = mPlayer.IsUnderWater ? 1 : mPlayer.IsUnderLava ? 2 : 0;
        mWorldShader.SetInt("fluidType", fluidType);

        mWorldTexture.Use(TextureUnit.Texture0);
        mWorldShader.SetInt("blockTexture", 0);

        mWorldShader.SetFloat("alphaOverride", 0.0f);
        mWorld!.Render(mPlayer.Camera);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(-1f, -1f);
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);

        mWorldShader.SetFloat("alphaOverride", 0.7f);
        mWorld.RenderTransparent(mPlayer.Camera);

        GL.Enable(EnableCap.CullFace);
        GL.DepthMask(true);
        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.Disable(EnableCap.Blend);
        mWorldShader.SetFloat("alphaOverride", 0.0f);
    }

    // night(0.3,0.3,0.5) -> sunset(1,0.6,0.3) -> day(1,1,0.95)
    private static Vector3 GetSunColor(float dayFactor)
    {
        if (dayFactor > 0.5f)
            return Vector3.Lerp(new Vector3(1f, 0.6f, 0.3f), new Vector3(1f, 1f, 0.95f), (dayFactor - 0.5f) * 2f);
        return Vector3.Lerp(new Vector3(0.3f, 0.3f, 0.5f), new Vector3(1f, 0.6f, 0.3f), dayFactor * 2f);
    }

    private void RenderShadows(Matrix4 view, Matrix4 proj)
    {
        var allEntities = mWorld!.Entities.Append(mPlayer!);
        mBlobShadowRenderer.Render(allEntities, mWorld, view, proj, mPlayer.Camera.Position);
    }

    private void RenderEntities(Matrix4 view, Matrix4 proj)
    {
        Entity.CameraPosition = mPlayer!.Camera.Position;
        mWorld!.RenderEntities(view, proj, mPlayer.Camera.Position, mPlayer.Camera.RenderDistance);
    }

    private void RenderPaintings(Matrix4 view, Matrix4 proj)
    {
        var paintings = mWorld!.Entities.OfType<PaintingEntity>();
        mPaintingRenderer.Render(paintings, mPaintingsTexture, view, proj);
    }

    private void RenderParticles(Matrix4 view, Matrix4 proj)
    {
        GL.DepthMask(false);
        mParticleSystem.Render(view, proj, mWorldTexture);
        mParticleSystem.RenderSmoke(view, proj);
        GL.DepthMask(true);
    }

    private void RenderBlockHighlight(Matrix4 view, Matrix4 proj)
    {
        var hit = mWorld!.Raycast(mPlayer!.Camera.Position, mPlayer.Camera.Front);

        if (hit.Type == RaycastHitType.Block)
        {
            var boundsMin = BlockRegistry.GetBoundsMin(hit.BlockType);
            var boundsMax = BlockRegistry.GetBoundsMax(hit.BlockType);

            if (hit.BlockType == BlockType.Torch)
            {
                int meta = mWorld.GetMetadata(hit.BlockPos.X, hit.BlockPos.Y, hit.BlockPos.Z);
                if (meta > 0)
                    (boundsMin, boundsMax) = BlockTorch.GetWallTorchBounds(meta - 1);
            }

            mBlockHighlight.Render(hit.BlockPos, view, proj, boundsMin, boundsMax);
        }

        int breakStage = mPlayer.GetBreakStage();
        if (breakStage >= 0 && mPlayer.BreakingBlockPos.HasValue)
            mBlockBreakOverlay.Render(mPlayer.BreakingBlockPos.Value, breakStage, view, proj, mBreakTexture);
    }

    private void RenderHud()
    {
        GL.Disable(EnableCap.DepthTest);
        mCrosshair.Render();
        GL.Enable(EnableCap.DepthTest);
    }

    private void RenderFireOverlay(System.Numerics.Vector2 display)
    {
        var fireUv = UvHelper.FromTileCoords(6, 7);
        var uvMin = new System.Numerics.Vector2(fireUv.TopLeft.X, fireUv.BottomRight.Y);
        var uvMax = new System.Numerics.Vector2(fireUv.BottomRight.X, fireUv.TopLeft.Y);
        uint tint = 0x990066FF;

        ImGui.GetBackgroundDrawList().AddImage(
            new IntPtr(mWorldTexture.Handle),
            System.Numerics.Vector2.Zero,
            display,
            uvMin, uvMax,
            tint);
    }

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
