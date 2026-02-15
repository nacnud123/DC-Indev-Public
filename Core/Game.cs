// Main game file, does things like generate world and init stuff and move between states | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VoxelEngine.Audio;
using VoxelEngine.GameEntity;
using VoxelEngine.Particles;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.UI;

namespace VoxelEngine.Core;

public enum GameState
{
    Playing,
    Paused,
    MainMenu,
    Inventory,
    Loading
}

public class Game : GameWindow
{
    public static Game Instance { get; private set; } = null!;

    #region Vars
    
    // Constants
    private const int INITIAL_PIG_COUNT = 5;
    private const int PIG_SPAWN_RADIUS = 20;

    // Core Systems
    private World mWorld = null!;
    private Player mPlayer = null!;
    private TickSystem mTickSystem = null!;

    // Rendering
    private Shader mShader = null!;
    private Crosshair mCrosshair = null!;
    private BlockHighlight mBlockHighlight = null!;
    private BlockBreakOverlay mBlockBreakOverlay = null!;
    private Texture mBreakTexture = null!;
    public Texture WorldTexture { get; private set; } = null!;
    public ParticleSystem ParticleSystem { get; private set; } = null!;

    // UI
    private ImGuiController mImGuiController = null!;
    private PauseScreen mPauseScreen = null!;
    private LoadingScreen mLoadingScreen = null!;
    private MainMenuScreen mMainMenuScreen = null!;
    private InventoryScreen mInventoryScreen = null!;
    private Hotbar mHotbar = null!;

    internal Hotbar Hotbar => mHotbar;

    // Input State
    private bool mWireframeMode;
    private bool mCursorGrabbed = true;
    private bool mFirstMouse = true;
    private Vector2 mLastMousePos;

    // Debug / Stats
    private double mFpsTimer;
    private int mFrameCount;
    private int mTickCount;

    private int mNewWorldSize = 64;
    private int mLoadingFrames;
    
    // Audio
    private AudioManager mAudioManager;
    public AudioManager AudioManager { get => mAudioManager; }

    // Day/Night cycle
    private float mTimeOfDay = 0.1f; // 0=dawn, 0.25=noon, 0.5=dusk, 0.75=midnight
    private const float DAY_LENGTH = 600f; // 10 minutes full cycle

    // Player
    private Vector3 mSpawnPos;
    public Player GetPlayer { get => mPlayer; }

    // Structures
    private readonly StructureLoader mStructureLoader = new();

    // Structure export selection corners
    private Vector3i? mExportCorner1;
    private Vector3i? mExportCorner2;

    // Global Random
    private Random mGameRandom = new Random();
    public Random GameRandom { get => mGameRandom; }

    // Game State
    public GameState CurrentState { get; private set; }
    
    #endregion
    
    public Game(int width, int height, string title)
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new Vector2i(width, height),
            Title = title,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
        })
    {
        mGameRandom = new Random();
        mAudioManager = new AudioManager();
    }

    #region Life

    protected override void OnLoad()
    {
        base.OnLoad();
        Instance = this;

        InitGl();
        LoadResources();
        InitUi();

        CurrentState = GameState.MainMenu;
        CursorState = CursorState.Normal;
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);

        if (mPlayer != null)
            mPlayer.Camera.AspectRatio = e.Width / (float)e.Height;

        mImGuiController?.WindowResized(ClientSize.X, ClientSize.Y);
    }

    // Dispose of stuff to free up memory
    protected override void OnUnload()
    {
        base.OnUnload();

        mWorld?.Dispose();
        mShader?.Dispose();
        mCrosshair?.Dispose();
        mBlockHighlight?.Dispose();
        mBlockBreakOverlay?.Dispose();
        mBreakTexture?.Dispose();
        WorldTexture?.Dispose();
        ParticleSystem?.Dispose();

        EntityModel.DisposeAll();
        Entity.DisposeShader();

        mImGuiController?.Dispose();
    }

    #endregion

    #region Inits

    private void InitGl()
    {
        GL.ClearColor(0.5f, 0.7f, 1.0f, 1.0f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
        GL.FrontFace(FrontFaceDirection.Ccw);
    }

    // Load resources and shaders in
    private void LoadResources()
    {
        mShader = new Shader( File.ReadAllText("Shaders/vertex.glsl"), File.ReadAllText("Shaders/fragment.glsl"));
        WorldTexture = Texture.LoadFromFile("Resources/world.png");

        mCrosshair = new Crosshair();
        mBlockHighlight = new BlockHighlight();
        mBlockBreakOverlay = new BlockBreakOverlay();
        mBreakTexture = Texture.LoadFromFile("Resources/break.png");

        mTickSystem = new TickSystem();
        ParticleSystem = new ParticleSystem();
    }

    // Actually start the world generation
    private void InitWorld()
    {
        mWorld = new World(mNewWorldSize);
        mWorld.BuildAllMeshes();

        int spawnX = mWorld.SizeInChunks * Chunk.WIDTH / 2;
        int spawnZ = mWorld.SizeInChunks * Chunk.DEPTH / 2;
        mSpawnPos = mWorld.FindSpawnPosition(spawnX, spawnZ);
        mPlayer = new Player(mSpawnPos, Size.X / (float)Size.Y);

        mHotbar = new Hotbar(WorldTexture);
        mHotbar.SetBlockInSlot(0, BlockType.Grass);
        mHotbar.SetBlockInSlot(1, BlockType.Dirt);
        mHotbar.SetBlockInSlot(2, BlockType.Stone);
        mHotbar.SetBlockInSlot(3, BlockType.Wood);
        mHotbar.SetBlockInSlot(4, BlockType.Leaves);
        mHotbar.SetBlockInSlot(5, BlockType.Sand);
        mHotbar.SetBlockInSlot(6, BlockType.Glowstone);
        mHotbar.SetBlockInSlot(7, BlockType.Glass);
        mHotbar.SetBlockInSlot(8, BlockType.Torch);
        mHotbar.SetBlockInSlot(9, BlockType.YellowFlower);
        mPlayer.SelectedBlock = mHotbar.GetSelectedBlock() ?? BlockType.Grass;
        
        SpawnSomePigs();
        SpawnSomeSheep();
        
        // Structure loading
        var house = mStructureLoader.Load("SpawnHouse.json");
        mStructureLoader.Place(mWorld, house, (int)mSpawnPos.X - (house.SizeX / 2), (int)mSpawnPos.Y-1, (int)mSpawnPos.Z - (house.SizeZ / 2));
        
        var tower = mStructureLoader.Load("tower.json");
        mStructureLoader.PlaceRandomly(mWorld, tower, Vector3i.Zero);

        var pyramid = mStructureLoader.Load("pyramid.json");
        mStructureLoader.PlaceRandomly(mWorld, pyramid, Vector3i.Zero);

        var obelisk = mStructureLoader.Load("obelisk.json");
        mStructureLoader.PlaceRandomly(mWorld, obelisk, Vector3i.Zero);

        var fountain = mStructureLoader.Load("fountain.json");
        mStructureLoader.PlaceRandomly(mWorld, fountain, new Vector3i(0, 2, 0));

        var dungeon = mStructureLoader.Load("dungeon.json");
        mStructureLoader.PlaceUnderground(mWorld, dungeon, changeRandomBlocks: true, rndOriginalType: BlockType.CobbleStone, rndNewType: BlockType.MossyCobblestone, rndChance: .5f);
    

    }

    private void SpawnSomePigs()
    {
        for (int i = 0; i < INITIAL_PIG_COUNT; i++)
        {
            int offsetX = mGameRandom.Next(-PIG_SPAWN_RADIUS, PIG_SPAWN_RADIUS);
            int offsetZ = mGameRandom.Next(-PIG_SPAWN_RADIUS, PIG_SPAWN_RADIUS);

            int x = (int)mSpawnPos.X + offsetX;
            int z = (int)mSpawnPos.Z + offsetZ;

            Vector3 pigSpawn = mWorld.FindSpawnPosition(x, z);
            mWorld.AddEntity(new Pig(pigSpawn));
        }
    }
    
    private void SpawnSomeSheep()
    {
        for (int i = 0; i < INITIAL_PIG_COUNT; i++)
        {
            int offsetX = mGameRandom.Next(-PIG_SPAWN_RADIUS, PIG_SPAWN_RADIUS);
            int offsetZ = mGameRandom.Next(-PIG_SPAWN_RADIUS, PIG_SPAWN_RADIUS);

            int x = (int)mSpawnPos.X + offsetX;
            int z = (int)mSpawnPos.Z + offsetZ;

            Vector3 sheepSpawn = mWorld.FindSpawnPosition(x, z);
            mWorld.AddEntity(new Sheep(sheepSpawn));
        }
    }

    // Load in the UI screens
    private void InitUi()
    {
        mImGuiController = new ImGuiController(ClientSize.X, ClientSize.Y);
        mPauseScreen = new PauseScreen();
        mLoadingScreen = new LoadingScreen();
        mMainMenuScreen = new MainMenuScreen();
        mInventoryScreen = new InventoryScreen(WorldTexture);

        mPauseScreen.OnPauseQuitGame += ReturnToMainMenu;
        mPauseScreen.OnResumeGame += ResumeGame;

        mMainMenuScreen.OnTitleQuitGame += Close;
        mMainMenuScreen.OnStartGame += StartGame;
    }

    #endregion

    #region Update

    
    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        float dt = (float)args.Time; // Delta time
        mImGuiController.Update(this, dt);

        // State management
        switch (CurrentState)
        {
            case GameState.MainMenu:
                mMainMenuScreen?.Render();
                return;

            case GameState.Loading:
                mLoadingScreen?.Render();
                mLoadingFrames++;
                
                // Wait a few frames so the loading screen actually renders
                if (mLoadingFrames >= 3)
                {
                    InitWorld();
                    CurrentState = GameState.Playing;
                    CursorState = CursorState.Grabbed;
                }
                return;

            case GameState.Paused:
                if (KeyboardState.IsKeyPressed(Keys.Escape))
                {
                    ResumeGame();
                    return;
                }
                mPauseScreen?.Render();
                return;

            case GameState.Inventory:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keys.E))
                {
                    CloseInventory();
                    return;
                }
                mInventoryScreen?.Render();
                return;

            case GameState.Playing:
                if (KeyboardState.IsKeyPressed(Keys.Escape))
                {
                    PauseGame();
                    return;
                }
                if (KeyboardState.IsKeyPressed(Keys.E))
                {
                    OpenInventory();
                    return;
                }
                ProcessInput(dt);
                UpdateGameLogic(dt);
                mHotbar?.Render();
                break;
        }
    }

    private void ProcessInput(float dt)
    {
        // Debug keys
        if (KeyboardState.IsKeyPressed(Keys.X))
        {
            mWireframeMode = !mWireframeMode;
            GL.PolygonMode(MaterialFace.FrontAndBack, mWireframeMode ? PolygonMode.Line : PolygonMode.Fill);
        }

        if (KeyboardState.IsKeyPressed(Keys.R))
            mPlayer.ResetPosition();

        if (KeyboardState.IsKeyPressed(Keys.P))
        {
            var pig = new Pig(new Vector3(mPlayer.Position.X, mPlayer.Position.Y + 5, mPlayer.Position.Z));
            mWorld.AddEntity(pig);
        }
        
        if (KeyboardState.IsKeyPressed(Keys.Tab))
        {
            mCursorGrabbed = !mCursorGrabbed;
            CursorState = mCursorGrabbed ? CursorState.Grabbed : CursorState.Normal;
            mFirstMouse = true;
        }

        // Structure saving
        /*
        if (KeyboardState.IsKeyPressed(Keys.F1))
        {
            var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);
            if (hit.Type == RaycastHitType.Block)
            {
                mExportCorner1 = hit.BlockPos;
                Title = $"Export: Corner 1 set to ({hit.BlockPos.X}, {hit.BlockPos.Y}, {hit.BlockPos.Z})";
            }
        }

        if (KeyboardState.IsKeyPressed(Keys.F2))
        {
            var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);
            if (hit.Type == RaycastHitType.Block)
            {
                mExportCorner2 = hit.BlockPos;
                if (mExportCorner1.HasValue)
                {
                    string file = StructureLoader.Export(mWorld, mExportCorner1.Value, mExportCorner2.Value);
                    Title = $"Export: Saved to {file}";
                    mExportCorner1 = null;
                    mExportCorner2 = null;
                }
                else
                {
                    Title = "Export: Corner 2 set, but corner 1 not set. Press F1 first.";
                }
            }
        }
        */
        
        if (KeyboardState.IsKeyPressed(Keys.Equal) || KeyboardState.IsKeyPressed(Keys.KeyPadAdd))
            mPlayer.Camera.RenderDistance = MathF.Min(mPlayer.Camera.RenderDistance + 32f, 512f);

        if (KeyboardState.IsKeyPressed(Keys.Minus) || KeyboardState.IsKeyPressed(Keys.KeyPadSubtract))
            mPlayer.Camera.RenderDistance = MathF.Max(mPlayer.Camera.RenderDistance - 32f, 32f);

        if (mCursorGrabbed)
        {
            var mousePos = new Vector2(MouseState.X, MouseState.Y);
            if (mFirstMouse)
            {
                mLastMousePos = mousePos;
                mFirstMouse = false;
            }

            mPlayer.HandleMouseLook(mousePos - mLastMousePos);
            mLastMousePos = mousePos;
        }

        // Hotbar slot selection
        if (KeyboardState.IsKeyPressed(Keys.D1)) SelectHotbarSlot(0);
        if (KeyboardState.IsKeyPressed(Keys.D2)) SelectHotbarSlot(1);
        if (KeyboardState.IsKeyPressed(Keys.D3)) SelectHotbarSlot(2);
        if (KeyboardState.IsKeyPressed(Keys.D4)) SelectHotbarSlot(3);
        if (KeyboardState.IsKeyPressed(Keys.D5)) SelectHotbarSlot(4);
        if (KeyboardState.IsKeyPressed(Keys.D6)) SelectHotbarSlot(5);
        if (KeyboardState.IsKeyPressed(Keys.D7)) SelectHotbarSlot(6);
        if (KeyboardState.IsKeyPressed(Keys.D8)) SelectHotbarSlot(7);
        if (KeyboardState.IsKeyPressed(Keys.D9)) SelectHotbarSlot(8);
        if (KeyboardState.IsKeyPressed(Keys.D0)) SelectHotbarSlot(9);

        mPlayer.Update(mWorld, KeyboardState, dt);

        // Continuous block breaking with held left mouse
        mPlayer.UpdateBreaking(mWorld, dt, MouseState.IsButtonDown(MouseButton.Left) && mCursorGrabbed);

        // Entity attacks on single click only
        if (MouseState.IsButtonPressed(MouseButton.Left))
        {
            var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);
            if (hit.Type == RaycastHitType.Entity)
                hit.Entity!.TakeDamage(10);
        }

        if (MouseState.IsButtonPressed(MouseButton.Right))
            mPlayer.HandleBlockInteraction(mWorld, false, true);
    }

    // Select a block on the hotbar using number keys
    private void SelectHotbarSlot(int slot)
    {
        mHotbar.SetHotbarSlot(slot);
        var block = mHotbar.GetSelectedBlock();
        if (block.HasValue)
            mPlayer.SelectedBlock = block.Value;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (CurrentState == GameState.Playing && mHotbar != null)
        {
            mHotbar.ScrollSlot(-(int)e.OffsetY);
            var block = mHotbar.GetSelectedBlock();
            if (block.HasValue)
                mPlayer.SelectedBlock = block.Value;
        }

        mImGuiController.MouseScroll(e.Offset);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        mImGuiController.PressChar((char)e.Unicode);
    }

    private void UpdateGameLogic(float dt)
    {
        // Advance day/night cycle
        mTimeOfDay += dt / DAY_LENGTH;
        if (mTimeOfDay >= 1f)
            mTimeOfDay -= 1f;

        // Run game ticks
        int ticks = mTickSystem.Accumulate(dt);
        for (int i = 0; i < ticks; i++)
        {
            mTickCount++;
            mWorld.TickEntities();
            mWorld.Update();
            mWorld.RandomDisplayUpdates(mPlayer.Position);
            mWorld.DoBlockTick();
        }

        // Update active particles
        ParticleSystem.Update(dt, mWorld);
        ParticleSystem.UpdateSmoke(dt, mWorld);
    }

    #endregion

    #region Render

    // Main render loop, render world, entities, particles, block highlights, and UI
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (mWorld != null && mPlayer != null)
        {
            UpdateTitle(args.Time);

            var view = mPlayer.Camera.GetViewMatrix();
            var proj = mPlayer.Camera.GetProjectionMatrix();

            RenderWorld(view, proj);
            RenderEntities(view, proj);
            RenderParticles(view, proj);
            RenderBlockHighlight(view, proj);
            RenderHud();
        }

        mImGuiController?.Render();
        SwapBuffers();
    }

    // Main render world function, gives shaders world data.
    private void RenderWorld(Matrix4 view, Matrix4 proj)
    {
        float sunAngle = mTimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0.05f, 1.0f);
        float dayFactor = (sunlightLevel - 0.05f) / 0.95f;

        Vector3 lightDir = new Vector3(-MathF.Cos(sunAngle), -MathF.Sin(sunAngle), -0.3f).Normalized();
        Vector3 lightColor = GetSunColor(dayFactor);
        Vector3 skyColor = Vector3.Lerp(new Vector3(0.01f, 0.01f, 0.05f), new Vector3(0.5f, 0.7f, 1.0f), dayFactor);
        float ambientStrength = 0.08f + dayFactor * 0.22f;

        Entity.LightDir = lightDir;
        Entity.AmbientStrength = ambientStrength + 0.1f;
        Entity.SunlightLevel = sunlightLevel;

        float fogDist = mPlayer.Camera.RenderDistance;

        mShader.Use();
        mShader.SetMatrix4("model", Matrix4.Identity);
        mShader.SetMatrix4("view", view);
        mShader.SetMatrix4("projection", proj);
        mShader.SetVector3("lightDir", lightDir);
        mShader.SetVector3("lightColor", lightColor);
        mShader.SetFloat("ambientStrength", ambientStrength);
        mShader.SetFloat("sunlightLevel", sunlightLevel);
        mShader.SetVector3("fogColor", skyColor);

        if (mPlayer.IsUnderWater)
        {
            mShader.SetFloat("fogStart", 2.0f);
            mShader.SetFloat("fogEnd", fogDist - 5f);
            
            mShader.SetVector3("fogColor", new Vector3(.05f, .1f, .3f));
            GL.ClearColor(.05f, .1f, .3f, 1.0f);
        }
        else
        {
            mShader.SetFloat("fogStart", fogDist * 0.4f);
            mShader.SetFloat("fogEnd", fogDist * 0.9f);
            mShader.SetVector3("fogColor", skyColor);
            GL.ClearColor(skyColor.X, skyColor.Y, skyColor.Z, 1.0f);
        }

        mShader.SetInt("underwater", mPlayer.IsUnderWater ? 1 : 0);

        WorldTexture.Use(TextureUnit.Texture0);
        mShader.SetInt("blockTexture", 0);

        // Opaque pass
        mShader.SetFloat("alphaOverride", 0.0f);
        mWorld.Render(mPlayer.Camera);

        // Transparent pass (water)
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(-1f, -1f);
        
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);
        
        mShader.SetFloat("alphaOverride", 0.7f);
        mWorld.RenderTransparent(mPlayer.Camera);
        
        GL.Enable(EnableCap.CullFace);
        GL.DepthMask(true);
        
        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.Disable(EnableCap.Blend);
        mShader.SetFloat("alphaOverride", 0.0f);
    }

    // night(0.3,0.3,0.5) -> sunset(1,0.6,0.3) -> day(1,1,0.95)
    private static Vector3 GetSunColor(float dayFactor)
    {
        if (dayFactor > 0.5f)
            return Vector3.Lerp(new Vector3(1f, 0.6f, 0.3f), new Vector3(1f, 1f, 0.95f), (dayFactor - 0.5f) * 2f);

        return Vector3.Lerp(new Vector3(0.3f, 0.3f, 0.5f), new Vector3(1f, 0.6f, 0.3f), dayFactor * 2f);
    }
    
    // Calls render the active entities
    private void RenderEntities(Matrix4 view, Matrix4 proj)
    {
        mWorld.RenderEntities(view, proj, mPlayer.Camera.Position, mPlayer.Camera.RenderDistance);
    }

    // Calls render on active particles
    private void RenderParticles(Matrix4 view, Matrix4 proj)
    {
        GL.DepthMask(false);
        ParticleSystem.Render(view, proj, WorldTexture);
        ParticleSystem.RenderSmoke(view, proj);
        GL.DepthMask(true);
    }

    // Render the block highlight on the active block
    private void RenderBlockHighlight(Matrix4 view, Matrix4 proj)
    {
        var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);

        if (hit.Type == RaycastHitType.Block)
        {
            var boundsMin = Terrain.Blocks.BlockRegistry.GetBoundsMin(hit.BlockType);
            var boundsMax = Terrain.Blocks.BlockRegistry.GetBoundsMax(hit.BlockType);
            mBlockHighlight.Render(hit.BlockPos, view, proj, boundsMin, boundsMax);
        }

        int breakStage = mPlayer.GetBreakStage();
        if (breakStage >= 0 && mPlayer.BreakingBlockPos.HasValue)
        {
            mBlockBreakOverlay.Render(mPlayer.BreakingBlockPos.Value, breakStage, view, proj, mBreakTexture);
        }
    }

    // Renders the hud
    private void RenderHud()
    {
        GL.Disable(EnableCap.DepthTest);
        mCrosshair.Render();
        GL.Enable(EnableCap.DepthTest);
    }

    // Update the title bar with debug information
    private void UpdateTitle(double deltaTime)
    {
        mFrameCount++;
        mFpsTimer += deltaTime;

        if (mFpsTimer >= 1.0)
        {
            string mode = mPlayer.IsFlying ? "FLY" : mPlayer.IsOnGround ? "WALK" : "AIR";
            if (mPlayer.IsSprinting)
                mode += "+SPRINT";

            var pos = mPlayer.Position;
            int chunkX = (int)(pos.X / Chunk.WIDTH);
            int chunkZ = (int)(pos.Z / Chunk.DEPTH);
            int renderDist = (int)mPlayer.Camera.RenderDistance;

            // Convert timeOfDay to 24h clock: 0.0=6:00(dawn), 0.25=12:00(noon), 0.5=18:00(dusk), 0.75=0:00(midnight)
            float hours24 = (mTimeOfDay * 24f + 6f) % 24f;
            int h = (int)hours24;
            int m = (int)((hours24 - h) * 60);
            string timeStr = $"{h:D2}:{m:D2}";

            Title = $"DuncanCraft 2000 InDev | FPS:{mFrameCount} TPS:{mTickCount} | {mPlayer.SelectedBlock} | {pos.X:F1},{pos.Y:F1},{pos.Z:F1} Chunk:{chunkX},{chunkZ} [{mode}] Render:{renderDist} Time:{timeStr}";

            mFrameCount = 0;
            mTickCount = 0;
            mFpsTimer = 0;
        }
    }

    #endregion

    #region Game State
    
    private void StartGame(int worldSize, int volumeSFX, int volumeMusic)
    {
        mNewWorldSize = worldSize;
        mAudioManager.SfxVol = volumeSFX;
        mAudioManager.MusicVol = volumeMusic;
        mAudioManager.PlayBackgroundMusic();
        mLoadingFrames = 0;
        CurrentState = GameState.Loading;
    }

    private void PauseGame()
    {
        CurrentState = GameState.Paused;
        CursorState = CursorState.Normal;
        MousePosition = new Vector2(Size.X / 2f, Size.Y / 2f);
    }

    private void ResumeGame()
    {
        CurrentState = GameState.Playing;
        CursorState = CursorState.Grabbed;
        mFirstMouse = true;
    }

    private void OpenInventory()
    {
        CurrentState = GameState.Inventory;
        CursorState = CursorState.Normal;
        MousePosition = new Vector2(Size.X / 2f, Size.Y / 2f);
    }

    public void CloseInventory()
    {
        CurrentState = GameState.Playing;
        CursorState = CursorState.Grabbed;
        mFirstMouse = true;
    }

    private void ReturnToMainMenu()
    {
        mWorld?.Dispose();
        mWorld = null!;
        mPlayer = null!;

        CurrentState = GameState.MainMenu;
        CursorState = CursorState.Normal;

        mAudioManager?.Stop();
    }

    #endregion
}
