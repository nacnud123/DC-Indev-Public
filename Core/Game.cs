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
    DoubleChest,
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
    private GameRenderer mRenderer = null!;
    public Texture WorldTexture { get; private set; } = null!;
    public Texture ItemTexture { get; private set; } = null!;
    public Texture IconsTexture { get; private set; } = null!;
    public Texture PaintingsTexture { get; private set; } = null!;
    public ParticleSystem ParticleSystem { get; private set; } = null!;
    private BlockIconRenderer mBlockIconRenderer = null!;

    // UI
    private ImGuiController mImGuiController = null!;
    private PauseScreen mPauseScreen = null!;
    private LoadingScreen mLoadingScreen = null!;
    private MainMenuScreen mMainMenuScreen = null!;
    private InventoryScreen mInventoryScreen = null!;
    private CreativeInventoryScreen mCreativeInventoryScreen = null!;
    private CraftingScreen mCraftingScreen = null!;
    private FurnaceScreen mFurnaceScreen = null!;
    private ChestScreen mChestScreen = null!;
    private DoubleChestScreen mDoubleChestScreen = null!;
    private DeathScreen mDeathScreen = null!;
    private Hotbar mHotbar = null!;
    private HudScreen mHudScreen = null!;
    private PlayerInventory mInventory = null!;

    internal Hotbar Hotbar => mHotbar;
    public PlayerInventory? PlayerInventory => mInventory;
    public bool IsCreative { get; private set; }

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

    private AsciiFramebuffer mAsciiFbo;
    private Shader mAsciiShader;
    private FullscreenQuad mFsQuad;
    private int mAsciiAtlas;
    private int mAsciiCharCount;
    public bool AsciiEnabled { get; set; } = false;

    public Random GameRandom
    {
        get => mGameRandom;
    }

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
        Keybindings.Load();

        CurrentState = GameState.MainMenu;
        CursorState = CursorState.Normal;

        mAsciiFbo = new AsciiFramebuffer(Size.X, Size.Y);
        mAsciiShader = new Shader(File.ReadAllText("Shaders/ascii.vert"), File.ReadAllText("Shaders/ascii.frag"));
        mFsQuad = new FullscreenQuad();
        (mAsciiAtlas, mAsciiCharCount) = LoadAsciiAtlas();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);

        if (mPlayer != null)
            mPlayer.Camera.AspectRatio = e.Width / (float)e.Height;

        if (mAsciiFbo != null)
        {
            mAsciiFbo.Dispose();
            mAsciiFbo = new AsciiFramebuffer(e.Width, e.Height);
        }

        mImGuiController?.WindowResized(ClientSize.X, ClientSize.Y);
    }

    // Dispose of stuff to free up memory
    protected override void OnUnload()
    {
        base.OnUnload();

        mWorld?.Dispose();
        mShader?.Dispose();
        WorldTexture?.Dispose();
        ItemTexture?.Dispose();
        IconsTexture?.Dispose();
        PaintingsTexture?.Dispose();
        mBlockIconRenderer?.Dispose();
        ParticleSystem?.Dispose();
        mRenderer?.Dispose();

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

        mBlockIconRenderer = new BlockIconRenderer();
        mBlockIconRenderer.Init(WorldTexture);

        mTickSystem = new TickSystem();
        ParticleSystem = new ParticleSystem();

        mRenderer = new GameRenderer();
        mRenderer.Init(mShader, WorldTexture, PaintingsTexture, ParticleSystem);

        Entity.SharedWorldTexture = WorldTexture;
        Entity.SharedRandom = mGameRandom;
        Entity.PlayStepSoundCallback = mAudioManager.PlayBlockContactSound;
    }

    // Actually start the world generation
    private void InitWorld()
    {
        bool isNewWorld = !Serialization.HasSavedChunks(Serialization.WorldName);

        var worldData = Serialization.LoadWorldData(Serialization.WorldName)
                        ?? Serialization.CreateWorld(
                            Serialization.WorldName,
                            customSeed: null,
                            worldSize: mNewWorldSize,
                            worldType: (int)mWorldGenSettings.Type,
                            worldTheme: (int)mWorldGenSettings.Theme
                        );


        IsCreative = worldData.IsCreative;
        this.mTimeOfDay = worldData.WorldTime;

        mTimeOfDay -= MathF.Floor(mTimeOfDay);
        if (mTimeOfDay < 0f)
            mTimeOfDay += 1f;

        mWorldGenSettings = WorldGenSettings.Build(worldData.WorldType, worldData.WorldTheme);

        mRenderer.ResetCloudOffset();

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
        mRenderer.SetSession(mWorld, mPlayer, mTickSystem, mPlayerArm, mHotbar, mHudScreen);

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
        mCreativeInventoryScreen = new CreativeInventoryScreen(mBlockIconRenderer, ItemTexture);
        mCraftingScreen = new CraftingScreen(mBlockIconRenderer, ItemTexture);
        mFurnaceScreen = new FurnaceScreen(mBlockIconRenderer, ItemTexture);
        mChestScreen = new ChestScreen(mBlockIconRenderer, ItemTexture);
        mDoubleChestScreen = new DoubleChestScreen(mBlockIconRenderer, ItemTexture);
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
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keybindings.Inventory))
                {
                    CloseInventory();
                    return;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) return;
                if (IsCreative)
                    mCreativeInventoryScreen?.Render();
                else
                    mInventoryScreen?.Render();
                return;

            case GameState.Crafting:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keybindings.Inventory))
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
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keybindings.Inventory))
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
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keybindings.Inventory))
                {
                    CloseChest();
                    return;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) return;
                mChestScreen?.Render();
                mHotbar?.Render();
                return;

            case GameState.DoubleChest:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keybindings.Inventory))
                {
                    CloseDoubleChest();
                    return;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) return;
                mDoubleChestScreen?.Render();
                mHotbar?.Render();
                return;

            case GameState.Playing:
                if (KeyboardState.IsKeyPressed(Keys.Escape))
                {
                    PauseGame();
                    return;
                }

                if (KeyboardState.IsKeyPressed(Keybindings.Inventory))
                {
                    OpenInventory();
                    return;
                }

                ProcessInput(dt);
                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) return;
                mHotbar?.Render();
                mRenderer.RenderHudOverlay();
                break;
        }
    }

    private void ProcessInput(float dt)
    {
        // Sync every frame — slot contents can change via inventory screen without a slot-change event.
        mPlayer.SelectedBlock = mHotbar?.GetSelectedBlock() ?? BlockType.Air;

        // Debug keys
        if (KeyboardState.IsKeyPressed(Keybindings.Wireframe))
        {
            mWireframeMode = !mWireframeMode;
            GL.PolygonMode(MaterialFace.FrontAndBack, mWireframeMode ? PolygonMode.Line : PolygonMode.Fill);
        }

        if (KeyboardState.IsKeyPressed(Keybindings.ResetPosition))
            mPlayer.ResetPosition();

        if (KeyboardState.IsKeyPressed(Keys.F9))
            mTimeOfDay = 0.75f;

        // Big hostile spawn burst (tries many candidates instantly)
        if (KeyboardState.IsKeyPressed(Keys.F10))
        {
            int spawned = mMobSpawner.DebugSpawnHostilesNow(candidateCount: 2000, ignoreCap: true);
            Title = $"Spawn test: spawned {spawned} hostiles (F9=midnight, F10=burst)";
        }

        if (KeyboardState.IsKeyPressed(Keybindings.ToggleCursor))
        {
            mCursorGrabbed = !mCursorGrabbed;
            CursorState = mCursorGrabbed ? CursorState.Grabbed : CursorState.Normal;
            mFirstMouse = true;
        }

        if (KeyboardState.IsKeyPressed(Keybindings.Screenshot))
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

        if (KeyboardState.IsKeyPressed(Keybindings.RenderDistUp))
            mPlayer.Camera.RenderDistance = MathF.Min(mPlayer.Camera.RenderDistance + 32f, 512f);

        if (KeyboardState.IsKeyPressed(Keybindings.RenderDistDown))
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

        if (KeyboardState.IsKeyPressed(Keybindings.DropItem))
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
            {
                hit.Entity!.TakeDamage(10);

                var knockDir = hit.Entity.Position - mPlayer.Position;
                knockDir.Y = 0;
                if (knockDir.LengthSquared > 0.001f)
                    knockDir.Normalize();
                hit.Entity.Velocity += new OpenTK.Mathematics.Vector3(knockDir.X, 0.7f, knockDir.Z) * 14f;
            }
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
        Entity.ListenerPosition = mPlayer.Position;
        Entity.SfxVol = mAudioManager.SfxVol;

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
            mRenderer.TickClouds();
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

        if (mWorld != null && mPlayer != null)
        {
            UpdateTitle(args.Time);

            if (AsciiEnabled)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, mAsciiFbo.Fbo);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                mRenderer.RenderFrame(mTimeOfDay, mWorldGenSettings);

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit);
                GL.Disable(EnableCap.DepthTest);

                mAsciiShader.Use();
                mAsciiShader.SetVector2("uScreenSize", new Vector2(Size.X, Size.Y));
                mAsciiShader.SetVector2("uCharSize", new Vector2(8, 8));
                mAsciiShader.SetInt("uCharCount", mAsciiCharCount);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, mAsciiFbo.ColorTexture);
                mAsciiShader.SetInt("uScene", 0);

                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, mAsciiAtlas);
                mAsciiShader.SetInt("uAsciiAtlas", 1);

                mFsQuad.Draw();
                GL.Enable(EnableCap.DepthTest);
            }
            else
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                mRenderer.RenderFrame(mTimeOfDay, mWorldGenSettings);
            }
        }
        else
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        mImGuiController?.Render();
        SwapBuffers();
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

    private (int texId, int charCount) LoadAsciiAtlas()
    {
        string chars = " .'`^\",:;il!i+_-?|/\\(){}[]<>tfjrxnuvczXYUJCLQ0OZmwqpdbkhaoegsyISTADEFGHKNPRVegsyIo*#MW&8%B@$23456791█";
        int charW = 8, charH = 8;

        var glyphs = new Dictionary<char, ulong>
        {
            [' '] = 0x0000000000000000UL,
            ['.'] = 0x0000000000003018UL,
            ['\''] = 0x0808080000000000UL,
            ['`'] = 0x1008000000000000UL,
            ['^'] = 0x183C660000000000UL,
            ['"'] = 0x6666220000000000UL,
            [','] = 0x0000000000183018UL,
            [':'] = 0x0000001818000000UL,
            [';'] = 0x0000001818083000UL,
            ['i'] = 0x0018001818181800UL,
            ['l'] = 0x1818181818181800UL,
            ['!'] = 0x1818181818001800UL,
            ['+'] = 0x00183C3C18000000UL,
            ['_'] = 0x000000000000FF00UL,
            ['-'] = 0x000000FF00000000UL,
            ['?'] = 0x3C42020C08000800UL,
            ['|'] = 0x0808080808080800UL,
            ['/'] = 0x0204081020408000UL,
            ['\\'] = 0x4020100804020000UL,
            ['('] = 0x0C181818180C0000UL,
            [')'] = 0x300C0C0C0C300000UL,
            ['{'] = 0x0E181830181E0000UL,
            ['}'] = 0x700C0C060C700000UL,
            ['['] = 0x3C303030303C0000UL,
            [']'] = 0x3C0C0C0C0C3C0000UL,
            ['<'] = 0x0C183060180C0000UL,
            ['>'] = 0x306018060C300000UL,
            ['t'] = 0x08083E0808080600UL,
            ['f'] = 0x0E18187E18181800UL,
            ['j'] = 0x0606060606663C00UL,
            ['r'] = 0x0000366E66606000UL,
            ['x'] = 0x0000663C183C6600UL,
            ['n'] = 0x00006E7666666600UL,
            ['u'] = 0x0000666666663E00UL,
            ['v'] = 0x0000666666663C00UL,
            ['c'] = 0x00003C6660603C00UL,
            ['z'] = 0x00007E060C187E00UL,
            ['X'] = 0x6666663C3C666600UL,
            ['Y'] = 0x6666663C18181800UL,
            ['U'] = 0x6666666666663C00UL,
            ['J'] = 0x1E0C0C0C6C6C3800UL,
            ['C'] = 0x3C66606060663C00UL,
            ['L'] = 0x60606060607E0000UL,
            ['Q'] = 0x3C66666676663C06UL,
            ['0'] = 0x3C66666E76663C00UL,
            ['O'] = 0x3C66666666663C00UL,
            ['Z'] = 0x7E060C1830607E00UL,
            ['m'] = 0x0000ECFED6D6C600UL,
            ['w'] = 0x0000C6C6D6FE6C00UL,
            ['q'] = 0x00003E66663E063EUL,
            ['p'] = 0x00006E76666E6060UL,
            ['d'] = 0x06063E6666663E00UL,
            ['b'] = 0x60606E7666667E00UL,
            ['k'] = 0x006066787866E600UL,
            ['h'] = 0x60606E7666666600UL,
            ['a'] = 0x00003C063E663E00UL,
            ['o'] = 0x00003C6666663C00UL,
            ['e'] = 0x00003C667E603C00UL,
            ['g'] = 0x00003E66663E063CUL,
            ['s'] = 0x00003C603C063C00UL,
            ['y'] = 0x00006666663E063CUL,
            ['I'] = 0x3C181818181818UL,
            ['S'] = 0x3C66603C06663C00UL,
            ['T'] = 0x7E181818181818UL,
            ['A'] = 0x183C667E66666600UL,
            ['D'] = 0x7C66666666667C00UL,
            ['E'] = 0x7E60607C60607E00UL,
            ['F'] = 0x7E60607C60606000UL,
            ['G'] = 0x3C66606E66663C00UL,
            ['H'] = 0x6666667E66666600UL,
            ['K'] = 0x666C7870786C6600UL,
            ['N'] = 0x66767E6E66666600UL,
            ['P'] = 0x7C66667C60606000UL,
            ['R'] = 0x7C66667C6C666600UL,
            ['V'] = 0x6666666666663C00UL,
            ['*'] = 0x0066663CFF3C6600UL,
            ['#'] = 0x367F36367F360000UL,
            ['M'] = 0xC6EEFED6C6C6C600UL,
            ['W'] = 0xC6C6C6D6FEEEC600UL,
            ['&'] = 0x386C6C3876DCCE76UL,
            ['8'] = 0x3C66663C66663C00UL,
            ['%'] = 0x6066060C18306600UL,
            ['B'] = 0x7C66667C66667C00UL,
            ['@'] = 0x3C66736B6B3E0000UL,
            ['$'] = 0x187E603C067E1800UL,
            ['2'] = 0x3C66060C18307E00UL,
            ['3'] = 0x3C66061C06663C00UL,
            ['4'] = 0x060E1E667F060600UL,
            ['5'] = 0x7E607C0606663C00UL,
            ['6'] = 0x1C30607C66663C00UL,
            ['7'] = 0x7E060C1830303000UL,
            ['9'] = 0x3C66663E06663C00UL,
            ['1'] = 0x0C1C0C0C0C0C3C00UL,
            ['█'] = 0xFFFFFFFFFFFFFFFFUL,
        };

        int width = chars.Length * charW;
        byte[] pixels = new byte[width * charH * 4]; // RGBA

        for (int ci = 0; ci < chars.Length; ci++)
        {
            ulong glyph = glyphs.TryGetValue(chars[ci], out var g) ? g : 0UL;
            for (int row = 0; row < charH; row++)
            {
                byte rowBits = (byte)((glyph >> (56 - row * 8)) & 0xFF);
                for (int col = 0; col < charW; col++)
                {
                    bool lit = (rowBits & (0x80 >> col)) != 0;
                    int idx = ((row * width) + (ci * charW + col)) * 4;
                    byte val = lit ? (byte)255 : (byte)0;
                    pixels[idx + 0] = val; // R
                    pixels[idx + 1] = val; // G
                    pixels[idx + 2] = val; // B
                    pixels[idx + 3] = val; // A
                }
            }
        }

        int texId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, texId);

        GL.TexImage2D(
            TextureTarget.Texture2D, 0,
            PixelInternalFormat.Rgba,
            width, charH, 0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
            PixelType.UnsignedByte,
            pixels);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        return (texId, chars.Length);
    }

    #endregion

    #region Game State

    private void StartGame(int worldSize, int volumeSFX, int volumeMusic, int worldType = 0, int worldTheme = 0,
        bool isCreative = false)
    {
        mNewWorldSize = worldSize;
        mWorldGenSettings = WorldGenSettings.Build(worldType, worldTheme);
        IsCreative = isCreative;
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
        if (IsCreative)
            mCreativeInventoryScreen?.OnClose();
        else
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

    public void OpenDoubleChest(OpenTK.Mathematics.Vector3i canonicalPos)
    {
        var chest = BlockEntityManager.GetOrCreateDoubleChest(canonicalPos);
        mDoubleChestScreen.SetChest(chest);
        CurrentState = GameState.DoubleChest;
        CursorState = CursorState.Normal;
        MousePosition = new Vector2(Size.X / 2f, Size.Y / 2f);
    }

    public void CloseDoubleChest()
    {
        mDoubleChestScreen?.OnClose();
        CurrentState = GameState.Playing;
        CursorState = CursorState.Grabbed;
        mFirstMouse = true;
    }

    private List<SavedEntity> SaveWorldEntities() =>
        WorldEntitySerializer.Save(mWorld.Entities);

    private void LoadWorldEntities(List<SavedEntity> saved) =>
        WorldEntitySerializer.Load(saved, mWorld, WorldTexture);

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
        var saveData = Serialization.LoadWorldData(Serialization.WorldName);
        if (saveData != null)
        {
            saveData.HasPlayerPosition = true;
            saveData.IsCreative = IsCreative;
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
            Serialization.UpdateLastPlayed(Serialization.WorldName);
        }
    }

    private void SaveGame() => SaveWorldToDisk();

    private void TeardownWorld()
    {
        mRenderer.ClearSession();
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