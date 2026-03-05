// Main game file, does things like generate world and init stuff and move between states | DA | 2/5/26

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VoxelEngine.Audio;
using VoxelEngine.BlockEntities;
using VoxelEngine.GameEntity;
using VoxelEngine.Particles;
using VoxelEngine.Rendering;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Items;
using ImGuiNET;
using VoxelEngine.UI;
using VoxelEngine.Utils;
using System.Linq;

namespace VoxelEngine.Core;

public enum GameState
{
    Playing,
    Paused,
    MainMenu,
    Inventory,
    Crafting,
    Furnace,
    Chest,
    Loading,
    Died
}

public class Game : GameWindow
{
    public static Game Instance { get; private set; } = null!;

    #region Vars

    // Core Systems
    private World mWorld = null!;
    private Player mPlayer = null!;
    private TickSystem mTickSystem = null!;
    private MobSpawner mMobSpawner = null!;

    public World GetWorld
    {
        get => mWorld;
    }

    // Rendering
    private Shader mShader = null!;
    private Crosshair mCrosshair = null!;
    private BlockHighlight mBlockHighlight = null!;
    private BlockBreakOverlay mBlockBreakOverlay = null!;
    private Texture mBreakTexture = null!;
    public Texture WorldTexture { get; private set; } = null!;
    public Texture ItemTexture { get; private set; } = null!;
    public Texture IconsTexture { get; private set; } = null!;
    public Texture PaintingsTexture { get; private set; } = null!;
    public ParticleSystem ParticleSystem { get; private set; } = null!;
    private BlockIconRenderer mBlockIconRenderer = null!;
    private PaintingRenderer mPaintingRenderer = null!;

    // UI
    private ImGuiController mImGuiController = null!;
    private PauseScreen mPauseScreen = null!;
    private LoadingScreen mLoadingScreen = null!;
    private MainMenuScreen mMainMenuScreen = null!;
    private InventoryScreen mInventoryScreen = null!;
    private CraftingScreen mCraftingScreen = null!;
    private FurnaceScreen mFurnaceScreen = null!;
    private ChestScreen mChestScreen = null!;
    private DeathScreen mDeathScreen = null!;
    private Hotbar mHotbar = null!;
    private HudScreen mHudScreen = null!;
    private PlayerInventory mInventory = null!;

    internal Hotbar Hotbar => mHotbar;
    public PlayerInventory? PlayerInventory => mInventory;

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
    private WorldGenSettings mWorldGenSettings = WorldGenSettings.Build(0, 0);

    public WorldGenSettings GetWorldGenSettings
    {
        get => mWorldGenSettings;
    }

    // Audio
    private AudioManager mAudioManager;

    public AudioManager AudioManager
    {
        get => mAudioManager;
    }

    // Day/Night cycle
    private float mTimeOfDay = 0.0f; // 0=dawn, 0.25=noon, 0.5=dusk, 0.75=midnight
    private const float DAY_LENGTH = 1200f; // 10 minutes full cycle
    public float TimeOfDay => mTimeOfDay;

    // Player
    private Vector3 mSpawnPos;
    private PlayerArm? mPlayerArm;

    public Player GetPlayer
    {
        get => mPlayer;
    }

    // Structures
    private readonly StructureLoader mStructureLoader = new();

    // Structure export selection corners
    private Vector3i? mExportCorner1;
    private Vector3i? mExportCorner2;

    // Global Random
    private Random mGameRandom = new Random();

    public Random GameRandom
    {
        get => mGameRandom;
    }

    // Game State
    public GameState CurrentState { get; private set; }

    // SkyBox
    private SkyRenderer mSkyRenderer = null!;

    // Clouds
    private CloudRenderer mCloudRenderer = null!;

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

        // On Wayland, GLFW doesn't support glfwSetCursorPos — suppress that specific error
        // so opening menus / pressing Escape doesn't crash.
        GLFWProvider.SetErrorCallback((code, desc) =>
        {
            if (desc != null && desc.Contains("cursor position"))
                return;
            throw new GLFWException(desc ?? string.Empty, code);
        });
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
        ItemTexture?.Dispose();
        IconsTexture?.Dispose();
        PaintingsTexture?.Dispose();
        mPaintingRenderer?.Dispose();
        mBlockIconRenderer?.Dispose();
        ParticleSystem?.Dispose();
        mSkyRenderer?.Dispose();
        mCloudRenderer?.Dispose();

        EntityModel.DisposeAll();
        ArrowEntity.DisposeMesh();
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
        mShader = new Shader(File.ReadAllText("Shaders/vertex.glsl"), File.ReadAllText("Shaders/fragment.glsl"));
        WorldTexture = Texture.LoadFromFile("Resources/world.png");

        ItemTexture = Texture.LoadFromFile("Resources/Items.png");
        IconsTexture = Texture.LoadFromFile("Resources/Icons.png");
        PaintingsTexture = Texture.LoadFromFile("Resources/Paintings.png");

        mPaintingRenderer = new PaintingRenderer();
        mPaintingRenderer.Init();

        mBlockIconRenderer = new BlockIconRenderer();
        mBlockIconRenderer.Init(WorldTexture);

        mCrosshair = new Crosshair();
        mBlockHighlight = new BlockHighlight();
        mBlockBreakOverlay = new BlockBreakOverlay();
        mBreakTexture = Texture.LoadFromFile("Resources/break.png");

        mTickSystem = new TickSystem();
        ParticleSystem = new ParticleSystem();

        mSkyRenderer = new SkyRenderer();
        mSkyRenderer.Init();

        mCloudRenderer = new CloudRenderer();
        mCloudRenderer.Init();
    }

    // Actually start the world generation
    private void InitWorld()
    {
        bool isNewWorld = !Serialization.HasSavedChunks(Serialization.s_WorldName);

        var worldData = Serialization.LoadWorldData(Serialization.s_WorldName)
                        ?? Serialization.CreateWorld(
                            Serialization.s_WorldName,
                            customSeed: null,
                            worldSize: mNewWorldSize,
                            worldType: (int)mWorldGenSettings.Type,
                            worldTheme: (int)mWorldGenSettings.Theme
                        );


        this.mTimeOfDay = worldData.WorldTime;
        
        mTimeOfDay -= MathF.Floor(mTimeOfDay);
        if (mTimeOfDay < 0f)
            mTimeOfDay += 1f;
        
        mWorldGenSettings = WorldGenSettings.Build(worldData.WorldType, worldData.WorldTheme);

        mCloudRenderer?.ResetOffset();

        mWorld = new World(mNewWorldSize, worldData.Seed, mWorldGenSettings);
        mWorld.BuildAllMeshes();

        int spawnX = mWorld.SizeInChunks * Chunk.WIDTH / 2;
        int spawnZ = mWorld.SizeInChunks * Chunk.DEPTH / 2;
        mSpawnPos = mWorld.FindSpawnPosition(spawnX, spawnZ);

        Vector3 playerPos = mSpawnPos;
        if (worldData.HasPlayerPosition)
            playerPos = new Vector3(worldData.PlayerX, worldData.PlayerY, worldData.PlayerZ);

        mPlayer = new Player(playerPos, Size.X / (float)Size.Y);
        mPlayerArm = new PlayerArm(WorldTexture, ItemTexture, "Resources/world.png", "Resources/Items.png");

        if (worldData.HasPlayerPosition)
            mPlayer.Camera.SetRotation(worldData.PlayerPitch, worldData.PlayerYaw);

        mInventory = new PlayerInventory();
        mHotbar = new Hotbar(mBlockIconRenderer, ItemTexture, mInventory);

        if (worldData.PlayerHealth > 0)
            mPlayer.Health = worldData.PlayerHealth;

        if (worldData.Inventory?.Count > 0)
        {
            mInventory.LoadFromSlots(worldData.Inventory);
        }
        else
        {
            // Default loadout for a fresh world
            /*mHotbar.SetItemInSlot(0, ItemType.StonePickaxe);
            mHotbar.SetItemInSlot(1, ItemType.StoneSword);
            mHotbar.SetItemInSlot(2, ItemType.StoneAxe);
            mHotbar.SetItemInSlot(3, ItemType.StoneHoe);
            mHotbar.SetItemInSlot(4, ItemType.Painting);
            mHotbar.SetItemInSlot(5, ItemType.Bow);
            mInventory.SetSlot(PlayerInventory.HOTBAR_START + 6, ItemStack.FromItem(ItemType.Arrow, 64));
            mHotbar.SetItemInSlot(7, ItemType.Seeds);
            mHotbar.SetItemInSlot(8, ItemType.Seeds);
            mInventory.SetSlot(0, ItemStack.FromItem(ItemType.FlintSteel));
            mInventory.SetSlot(1, ItemStack.FromBlock(BlockType.Torch, 32));
            */
        }

        mPlayer.SelectedBlock = mHotbar.GetSelectedBlock() ?? BlockType.Grass;

        BlockEntityManager.Load(Serialization.SaveLocation());

        if (worldData.Paintings?.Count > 0)
        {
            foreach (var sp in worldData.Paintings)
            {
                var art = PaintingRegistry.GetByName(sp.ArtName);
                var anchor = new Vector3i(sp.AnchorX, sp.AnchorY, sp.AnchorZ);
                mWorld.AddEntity(new PaintingEntity(anchor, sp.Facing, art));
            }
        }

        if (worldData.Entities?.Count > 0)
            LoadWorldEntities(worldData.Entities);

        mMobSpawner = new MobSpawner(mWorld, mGameRandom);

        // Place structures on new worlds and mark all chunks modified so they persist
        if (isNewWorld)
        {
            mStructureLoader.SeedRandom(worldData.Seed + 77777);

            var house = mStructureLoader.Load("SpawnHouse.json");
            mStructureLoader.Place(mWorld, house, (int)mSpawnPos.X - (house.SizeX / 2), (int)mSpawnPos.Y - 1,
                (int)mSpawnPos.Z - (house.SizeZ / 2));

            var tower = mStructureLoader.Load("tower.json");
            mStructureLoader.PlaceRandomly(mWorld, tower, Vector3i.Zero);

            var pyramid = mStructureLoader.Load("pyramid.json");
            mStructureLoader.PlaceRandomly(mWorld, pyramid, Vector3i.Zero);

            var obelisk = mStructureLoader.Load("obelisk.json");
            mStructureLoader.PlaceRandomly(mWorld, obelisk, Vector3i.Zero);

            var fountain = mStructureLoader.Load("fountain.json");
            mStructureLoader.PlaceRandomly(mWorld, fountain, new Vector3i(0, 2, 0));

            var dungeon = mStructureLoader.Load("dungeon.json");
            mStructureLoader.PlaceUnderground(mWorld, dungeon, changeRandomBlocks: true,
                rndOriginalType: BlockType.CobbleStone, rndNewType: BlockType.MossyCobblestone, rndChance: .5f);

            mWorld.MarkAllChunksWithBlocksAsModified();
        }
    }


    // Load in the UI screens
    private void InitUi()
    {
        mImGuiController = new ImGuiController(ClientSize.X, ClientSize.Y);
        mPauseScreen = new PauseScreen();
        mLoadingScreen = new LoadingScreen();
        mMainMenuScreen = new MainMenuScreen();
        mInventoryScreen = new InventoryScreen(mBlockIconRenderer, ItemTexture);
        mCraftingScreen = new CraftingScreen(mBlockIconRenderer, ItemTexture);
        mFurnaceScreen = new FurnaceScreen(mBlockIconRenderer, ItemTexture);
        mChestScreen = new ChestScreen(mBlockIconRenderer, ItemTexture);
        mHudScreen = new HudScreen(IconsTexture);
        mDeathScreen = new DeathScreen();

        mPauseScreen.OnPauseQuitGame += ReturnToMainMenu;
        mPauseScreen.OnResumeGame += ResumeGame;
        mPauseScreen.OnSaveGame += SaveGame;

        mDeathScreen.OnReturnToMainMenu += ReturnToMainMenuNoSave;

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

            case GameState.Died:
                mDeathScreen?.Render();
                return;

            case GameState.Inventory:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keys.E))
                {
                    CloseInventory();
                    return;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) return;
                mInventoryScreen?.Render();
                return;

            case GameState.Crafting:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keys.E))
                {
                    CloseCrafting();
                    return;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) return;
                mCraftingScreen?.Render();
                mHotbar?.Render();
                return;

            case GameState.Furnace:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keys.E))
                {
                    CloseFurnace();
                    return;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) return;
                mFurnaceScreen?.Render();
                mHotbar?.Render();
                return;

            case GameState.Chest:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keys.E))
                {
                    CloseChest();
                    return;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) return;
                mChestScreen?.Render();
                mHotbar?.Render();
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
                if (CurrentState == GameState.Died) return;
                mHotbar?.Render();
                RenderHudOverlay();
                break;
        }
    }

    private void ProcessInput(float dt)
    {
        // Sync every frame — slot contents can change via inventory screen without a slot-change event.
        mPlayer.SelectedBlock = mHotbar?.GetSelectedBlock() ?? BlockType.Air;

        // Debug keys
        if (KeyboardState.IsKeyPressed(Keys.X))
        {
            mWireframeMode = !mWireframeMode;
            GL.PolygonMode(MaterialFace.FrontAndBack, mWireframeMode ? PolygonMode.Line : PolygonMode.Fill);
        }

        if (KeyboardState.IsKeyPressed(Keys.R))
            mPlayer.ResetPosition();

        if (KeyboardState.IsKeyPressed(Keys.F9))
            mTimeOfDay = 0.75f;

        // Big hostile spawn burst (tries many candidates instantly)
        if (KeyboardState.IsKeyPressed(Keys.F10))
        {
            int spawned = mMobSpawner.DebugSpawnHostilesNow(candidateCount: 2000, ignoreCap: true);
            Title = $"Spawn test: spawned {spawned} hostiles (F9=midnight, F10=burst)";
        }

        if (KeyboardState.IsKeyPressed(Keys.Tab))
        {
            mCursorGrabbed = !mCursorGrabbed;
            CursorState = mCursorGrabbed ? CursorState.Grabbed : CursorState.Normal;
            mFirstMouse = true;
        }

        if (KeyboardState.IsKeyPressed(Keys.F7))
            TakeIsoScreenshot();

// Structure saving
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

        if (KeyboardState.IsKeyPressed(Keys.Q))
        {
            var selected = mHotbar.GetSelectedStack();
            if (selected.HasValue)
            {
                mInventory.ConsumeOne(PlayerInventory.HOTBAR_START + mHotbar.SelectedSlotIndex);
                if (!mHotbar.GetSelectedStack().HasValue)
                    mPlayer.SelectedBlock = BlockType.Air;

                var thrown = selected.Value.WithCount(1);
                var spawnPos = mPlayer.Camera.Position + mPlayer.Camera.Front * 0.5f;
                var vel = mPlayer.Camera.Front * 5f + new OpenTK.Mathematics.Vector3(0f, 2f, 0f);
                var drop = new DroppedItemEntity(spawnPos, thrown, WorldTexture);
                drop.Velocity = vel;
                mWorld.AddEntity(drop);
            }
        }

        mPlayer.Update(mWorld, KeyboardState, dt);

        mPlayerArm?.Update(dt, mPlayer.HorizontalSpeed);

        // Continuous block breaking with held left mouse
        bool holdingAttack = MouseState.IsButtonDown(MouseButton.Left) && mCursorGrabbed;
        mPlayer.UpdateBreaking(mWorld, dt, holdingAttack);

        if (holdingAttack)
            mPlayerArm?.TriggerSwing();

        // Entity attacks on single click only
        if (MouseState.IsButtonPressed(MouseButton.Left))
        {
            var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);
            if (hit.Type == RaycastHitType.Entity)
                hit.Entity!.TakeDamage(10);
        }

        if (MouseState.IsButtonPressed(MouseButton.Right))
        {
            mPlayerArm?.TriggerSwing();
            var selected = mHotbar.GetSelectedStack();
            mPlayer.HandleBlockInteraction(mWorld, false, true);
            if (selected.HasValue && !selected.Value.IsBlock)
                mPlayer.UseHeldItem(mWorld, selected.Value.Item);
        }
    }

    // Take a full isometric screenshot of the world and save to Documents
    private void TakeIsoScreenshot()
    {
        if (mWorld == null || mPlayer == null)
            return;

        Title = "Taking isometric screenshot...";
        var shooter = new IsoScreenshot(mWorld, mTimeOfDay);
        shooter.Capture();
        Title = "Screenshot saved! (check Documents folder)";
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
            mMobSpawner.Tick();
            mWorld.Update();
            mWorld.RandomDisplayUpdates(mPlayer.Position);
            mWorld.DoScheduledTick();
            mWorld.DoRandomTick();
            mCloudRenderer?.Tick();
            BlockEntityManager.TickFurnaces();
        }

        // Update active particles
        ParticleSystem.Update(dt, mWorld);
        ParticleSystem.UpdateSmoke(dt, mWorld);

        // Detect player death
        if (mPlayer.Health <= 0 && CurrentState != GameState.Died)
        {
            CurrentState = GameState.Died;
            CursorState = CursorState.Normal;
        }
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

            RenderSky(view, proj);
            RenderClouds(view, proj);
            RenderWorld(view, proj);
            RenderEntities(view, proj);
            RenderPaintings(view, proj);
            RenderParticles(view, proj);
            RenderBlockHighlight(view, proj);
            mPlayerArm?.Render(mPlayer.Camera, mHotbar.GetSelectedStack());
            RenderHud();
        }

        mImGuiController?.Render();
        SwapBuffers();
    }

    // Render the sky
    private void RenderSky(Matrix4 view, Matrix4 proj)
    {
        float sunAngle = mTimeOfDay * MathF.PI * 2f;
        float sunLightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, .05f, 1.0f);
        float dayFactor = (sunLightLevel - 0.05f) / 0.95f;

        mSkyRenderer.Render(
            mPlayer.Position,
            mTimeOfDay,
            mWorldGenSettings,
            dayFactor,
            view,
            proj);
    }

    // Render the sky
    private void RenderClouds(Matrix4 view, Matrix4 proj)
    {
        float sunAngle = mTimeOfDay * MathF.PI * 2f;
        float sunLightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, .05f, 1.0f);
        float dayFactor = (sunLightLevel - .05f) / .95f;

        float partialTick = mTickSystem.GetPartialTick();
        float fogDist = mPlayer.Camera.RenderDistance;

        Vector3 nightSky = mWorldGenSettings.DaySkyColor * 0.02f;
        Vector3 fogColor = Vector3.Lerp(nightSky, mWorldGenSettings.DayFogColor, dayFactor);

        mCloudRenderer.Render(
            mPlayer.Position,
            mWorldGenSettings,
            dayFactor,
            partialTick,
            fogColor,
            fogDist,
            view,
            proj);
    }

    // Main render world function, gives shaders world data.
    private void RenderWorld(Matrix4 view, Matrix4 proj)
    {
        float sunAngle = mTimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0.05f, 1.0f);
        float dayFactor = (sunlightLevel - 0.05f) / 0.95f;

        Vector3 lightDir = new Vector3(-MathF.Cos(sunAngle), -MathF.Sin(sunAngle), -0.3f).Normalized();
        Vector3 lightColor = GetSunColor(dayFactor);
        Vector3 nightSky = mWorldGenSettings.DaySkyColor * 0.02f;
        Vector3 skyColor = Vector3.Lerp(nightSky, mWorldGenSettings.DaySkyColor, dayFactor);
        Vector3 fogColor = Vector3.Lerp(nightSky, mWorldGenSettings.DayFogColor, dayFactor);
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
        mShader.SetVector3("fogColor", fogColor);

        if (mPlayer.IsUnderWater)
        {
            mShader.SetFloat("fogStart", 2.0f);
            mShader.SetFloat("fogEnd", fogDist - 5f);
            mShader.SetVector3("fogColor", new Vector3(.05f, .1f, .3f));
            GL.ClearColor(.05f, .1f, .3f, 1.0f);
        }
        else if (mPlayer.IsUnderLava)
        {
            mShader.SetFloat("fogStart", 0.5f);
            mShader.SetFloat("fogEnd", 3.0f);
            mShader.SetVector3("fogColor", new Vector3(.4f, .1f, .05f));
            GL.ClearColor(.4f, .1f, .05f, 1.0f);
        }
        else
        {
            mShader.SetFloat("fogStart", fogDist * 0.4f);
            mShader.SetFloat("fogEnd", fogDist * 0.9f);
            mShader.SetVector3("fogColor", fogColor);
            GL.ClearColor(skyColor.X, skyColor.Y, skyColor.Z, 1.0f);
        }

        int fluidType = mPlayer.IsUnderWater ? 1 : mPlayer.IsUnderLava ? 2 : 0;
        mShader.SetInt("fluidType", fluidType);

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

    private void RenderPaintings(Matrix4 view, Matrix4 proj)
    {
        var paintings = mWorld.Entities.OfType<PaintingEntity>();
        mPaintingRenderer.Render(paintings, PaintingsTexture, view, proj);
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

            // Wall torches have metadata-dependent bounds
            if (hit.BlockType == BlockType.Torch)
            {
                int meta = mWorld.GetMetadata(hit.BlockPos.X, hit.BlockPos.Y, hit.BlockPos.Z);
                if (meta > 0)
                    (boundsMin, boundsMax) = Terrain.Blocks.BlockTorch.GetWallTorchBounds(meta - 1);
            }

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

    private void RenderHudOverlay()
    {
        if (mHudScreen == null || mHotbar == null) return;
        var display = ImGui.GetIO().DisplaySize;
        mHudScreen.Render(
            mHotbar.GetHotbarX(display.X),
            mHotbar.GetHotbarY(display.Y),
            mHotbar.HotbarWidth);

        if (mPlayer.IsOnFire)
            RenderFireOverlay(display);
    }

    private void RenderFireOverlay(System.Numerics.Vector2 display)
    {
        var fireUv = UvHelper.FromTileCoords(6, 7);

        // UvHelper uses OpenGL bottom-left origin; ImGui expects top-left, so flip Y
        var uvMin = new System.Numerics.Vector2(fireUv.TopLeft.X, fireUv.BottomRight.Y);
        var uvMax = new System.Numerics.Vector2(fireUv.BottomRight.X, fireUv.TopLeft.Y);

        uint tint = 0x990066FF; // ABGR: semi-transparent orange-red

        ImGui.GetBackgroundDrawList().AddImage(
            new IntPtr(WorldTexture.Handle),
            System.Numerics.Vector2.Zero,
            display,
            uvMin, uvMax,
            tint);
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

            Title =
                $"DuncanCraft 2000 InDev | FPS:{mFrameCount} TPS:{mTickCount} | {mPlayer.SelectedBlock} | {pos.X:F1},{pos.Y:F1},{pos.Z:F1} Chunk:{chunkX},{chunkZ} [{mode}] Render:{renderDist} Time:{timeStr}";

            mFrameCount = 0;
            mTickCount = 0;
            mFpsTimer = 0;
        }
    }

    #endregion

    #region Game State

    private void StartGame(int worldSize, int volumeSFX, int volumeMusic, int worldType = 0, int worldTheme = 0)
    {
        mNewWorldSize = worldSize;
        mWorldGenSettings = WorldGenSettings.Build(worldType, worldTheme);
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

    private void DropCurrentBlock()
    {
    }

    public void CloseInventory()
    {
        mInventoryScreen?.OnClose();
        CurrentState = GameState.Playing;
        CursorState = CursorState.Grabbed;
        mFirstMouse = true;
    }

    public void OpenCrafting()
    {
        CurrentState = GameState.Crafting;
        CursorState = CursorState.Normal;
        MousePosition = new Vector2(Size.X / 2f, Size.Y / 2f);
    }

    public void CloseCrafting()
    {
        mCraftingScreen?.OnClose();
        CurrentState = GameState.Playing;
        CursorState = CursorState.Grabbed;
        mFirstMouse = true;
    }

    public void OpenFurnace(OpenTK.Mathematics.Vector3i pos)
    {
        var furnace = BlockEntityManager.GetOrCreateFurnace(pos);
        mFurnaceScreen.SetFurnace(furnace);
        CurrentState = GameState.Furnace;
        CursorState = CursorState.Normal;
        MousePosition = new Vector2(Size.X / 2f, Size.Y / 2f);
    }

    public void CloseFurnace()
    {
        mFurnaceScreen?.OnClose();
        CurrentState = GameState.Playing;
        CursorState = CursorState.Grabbed;
        mFirstMouse = true;
    }

    public void OpenChest(OpenTK.Mathematics.Vector3i pos)
    {
        var chest = BlockEntityManager.GetOrCreateChest(pos);
        mChestScreen.SetChest(chest);
        CurrentState = GameState.Chest;
        CursorState = CursorState.Normal;
        MousePosition = new Vector2(Size.X / 2f, Size.Y / 2f);
    }

    public void CloseChest()
    {
        mChestScreen?.OnClose();
        CurrentState = GameState.Playing;
        CursorState = CursorState.Grabbed;
        mFirstMouse = true;
    }

    // Serialize all saveable world entities to data objects for XML persistence
    private List<SavedEntity> SaveWorldEntities()
    {
        var list = new List<SavedEntity>();
        foreach (var entity in mWorld.Entities)
        {
            switch (entity)
            {
                case PaintingEntity:
                    break; // paintings are saved separately

                case Pig pig:
                    list.Add(MakeSavedMob("Pig", pig));
                    break;

                case Sheep sheep:
                    list.Add(MakeSavedMob("Sheep", sheep));
                    break;

                case Zombie zombie:
                    list.Add(MakeSavedMob("Zombie", zombie));
                    break;

                case Skeleton skeleton:
                    list.Add(MakeSavedMob("Skeleton", skeleton));
                    break;

                case Stalker stalker:
                    list.Add(MakeSavedMob("Stalker", stalker));
                    break;

                case DroppedItemEntity drop:
                    list.Add(new SavedEntity
                    {
                        Type = "DroppedItem",
                        X = drop.Position.X,
                        Y = drop.Position.Y,
                        Z = drop.Position.Z,
                        Stack = SerializableStack.From(drop.Stack),
                    });
                    break;
            }
        }

        return list;
    }

    private static SavedEntity MakeSavedMob(string type, Entity e) => new()
    {
        Type = type,
        X = e.Position.X,
        Y = e.Position.Y,
        Z = e.Position.Z,
        Yaw = e.Yaw,
        Health = e.Health,
    };

    // Reconstruct world entities from saved data and add them to the world
    private void LoadWorldEntities(List<SavedEntity> saved)
    {
        foreach (var se in saved)
        {
            var pos = new Vector3(se.X, se.Y, se.Z);

            Entity? entity = se.Type switch
            {
                "Pig" => new Pig(pos) { Yaw = se.Yaw, Health = se.Health },
                "Sheep" => new Sheep(pos) { Yaw = se.Yaw, Health = se.Health },
                "Zombie" => new Zombie(pos) { Yaw = se.Yaw, Health = se.Health },
                "Skeleton" => new Skeleton(pos) { Yaw = se.Yaw, Health = se.Health },
                "Stalker" => new Stalker(pos) { Yaw = se.Yaw, Health = se.Health },
                "DroppedItem" => se.Stack != null
                    ? new DroppedItemEntity(pos, se.Stack.ToItemStack(), WorldTexture)
                    : null,
                _ => null,
            };

            if (entity != null)
                mWorld.AddEntity(entity);
        }
    }

    private void SaveWorldToDisk()
    {
        int savedCount = 0;
        for (int x = 0; x < mWorld.GetChunks().GetLength(0); x++)
        {
            for (int z = 0; z < mWorld.GetChunks().GetLength(1); z++)
            {
                var tempChunk = mWorld.GetChunks()[x, z];
                if (tempChunk.HasChunkBeenModified)
                {
                    Serialization.SaveChunk(tempChunk);
                    savedCount++;
                }
            }
        }

        Console.WriteLine($"Saved {savedCount} modified chunks.");

        BlockEntityManager.Save(Serialization.SaveLocation());

        // Save player position and inventory
        var saveData = Serialization.LoadWorldData(Serialization.s_WorldName);
        if (saveData != null)
        {
            saveData.HasPlayerPosition = true;
            saveData.PlayerX = mPlayer.Position.X;
            saveData.PlayerY = mPlayer.Position.Y;
            saveData.PlayerZ = mPlayer.Position.Z;
            saveData.PlayerYaw = mPlayer.Camera.Yaw;
            saveData.PlayerPitch = mPlayer.Camera.Pitch;
            saveData.LastPlayed = DateTime.Now;
            saveData.WorldTime = mTimeOfDay;
            saveData.Inventory = mInventory.SaveToSlots();
            saveData.PlayerHealth = mPlayer.Health;
            saveData.Paintings = mWorld.Entities
                .OfType<PaintingEntity>()
                .Select(p => new SavedPainting
                {
                    AnchorX = p.AnchorPos.X,
                    AnchorY = p.AnchorPos.Y,
                    AnchorZ = p.AnchorPos.Z,
                    Facing = p.Facing,
                    ArtName = p.Art.Name,
                })
                .ToList();

            saveData.Entities = SaveWorldEntities();
            Serialization.SaveWorldMetadata(saveData);
        }
        else
        {
            Serialization.UpdateLastPlayed(Serialization.s_WorldName);
        }
    }

    private void SaveGame() => SaveWorldToDisk();

    private void TeardownWorld()
    {
        BlockEntityManager.Clear();
        mWorld?.Dispose();
        mWorld = null!;
        mPlayer = null!;

        CurrentState = GameState.MainMenu;
        CursorState = CursorState.Normal;
        mMainMenuScreen.ResetToTitle();

        mAudioManager?.Stop();
    }

    private void ReturnToMainMenu()
    {
        SaveWorldToDisk();
        TeardownWorld();
    }

    private void ReturnToMainMenuNoSave() => TeardownWorld();

    #endregion
}