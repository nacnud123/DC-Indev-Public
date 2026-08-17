// Main game file, does things like generate world and init stuff and move between states | DA | 2/5/26

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;

using VoxelEngine.Audio;
using VoxelEngine.BlockEntities;
using VoxelEngine.GameEntity;
using VoxelEngine.Particles;
using VoxelEngine.Rendering;
using VoxelEngine.Saving;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Infinite;
using VoxelEngine.Items;
using VoxelEngine.Net;
using ImGuiNET;
using VoxelEngine.UI;
using VoxelEngine.Utils;
using System.Linq;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;
using Shader = VoxelEngine.Rendering.Shader;

namespace VoxelEngine.Core;

// GameState moved to VoxelEngine.Common/Core/GameState.cs - shared interaction code
// branches on it, so it can't live in the client. Same namespace, so nothing else changed.

/// <summary>
/// Top-level owner of every engine subsystem: window/input, world/player, rendering,
/// audio, and all UI screens. Wires Silk.NET's window lifecycle events (Load/Update/
/// Render/Resize/Closing) to its own methods and is the single process-wide instance
/// accessed via <see cref="Instance"/>. Most cross-cutting game code (blocks, entities,
/// UI) reaches back into engine state through <c>Game.Instance</c> rather than being
/// passed references directly, so this class effectively acts as the service locator /
/// composition root for the whole engine.
/// </summary>
public partial class Game : IDisposable, IGameContext
{
    // Process-wide singleton, set at the top of OnLoad() once the window/GL context exist.
    // Other systems (blocks, entities, UI) reach through this rather than holding direct
    // references, since they're often instantiated far from where Game itself is created.
    public static Game Instance { get; private set; } = null!;

    #region Vars

    // Window and input
    private IWindow mWindow = null!;
    private IInputContext mInputContext = null!;
    private IKeyboard mKeyboard = null!;
    private IMouse mMouse = null!;

    // "Just pressed" tracking (cleared each frame)
    private readonly HashSet<SilkKey> mPressedThisFrame = new();
    private readonly HashSet<SilkMouseButton> mMouseButtonsPressed = new();
    private System.Numerics.Vector2 mMousePosition;
    private System.Numerics.Vector2 mLastMousePos;
    private bool mFirstMouse = true;

    // Core Systems
    private World mWorld = null!;
    private Player mPlayer = null!;
    private TickSystem mTickSystem = null!;
    private MobSpawner mMobSpawner = null!;

    public World GetWorld => mWorld;

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
    private ChatScreen mChatScreen = null!;
    private ConnectingScreen mConnectingScreen = null!;
    private DisconnectedScreen mDisconnectedScreen = null!;
    private Hotbar mHotbar = null!;
    private HudScreen mHudScreen = null!;
    private PlayerInventory mInventory = null!;

    // Null in singleplayer - that null is the "am I on a server?" test the rest of the client uses.
    // Only ever set once the connection is live AND the session is built, so anything that sees it
    // non-null can use it without further checks.
    private ClientNetwork? mNetwork;

    // The connection while ConnectAsync is still in flight. Separate from mNetwork because a
    // half-connected socket must not look like a live one.
    private ClientNetwork? mConnecting;

    // Set on the connect thread, consumed on the main thread - see PumpArrivingChunks.
    private volatile bool mLoginComplete;

    // The World owns it; we keep a handle so the MapChunk/PreChunk handlers can feed it.
    private NetworkChunkSource? mChunkSource;

    /// Name sent at login, from the Multiplayer screen's box. Beta had no auth: any name, any client.
    public string PlayerName => mMainMenuScreen?.PlayerName ?? "Player";

    internal Hotbar Hotbar => mHotbar;
    public PlayerInventory? PlayerInventory => mInventory;
    public bool IsCreative { get; private set; }

    // Input State
    private bool mWireframeMode;
    private bool mCursorGrabbed = true;

    // Debug / Stats
    private double mFpsTimer;
    private int mFrameCount;
    private int mTickCount;

    private int mLoadingFrames;
    private WorldGenSettings mWorldGenSettings = WorldGenSettings.Build(0);

    public WorldGenSettings GetWorldGenSettings => mWorldGenSettings;

    // Audio
    private AudioManager mAudioManager;
    public AudioManager AudioManager => mAudioManager;

    // Day/Night cycle
    // Normalized position in the day/night cycle, in [0,1): 0=dawn, 0.25=noon, 0.5=dusk,
    // 0.75=midnight. Advances by (deltaTime / DAY_LENGTH) each update and wraps via modulo.
    private float mTimeOfDay = 0.0f; // 0=dawn, 0.25=noon, 0.5=dusk, 0.75=midnight
    private const float DAY_LENGTH = 1200f; // 10 minutes full cycle, in seconds

    // Melee damage dealt with an empty hand or while holding a block, matching Item.AttackDamage's
    // default for items that aren't weapons.
    private const int BARE_HAND_DAMAGE = 1;
    public float TimeOfDay => mTimeOfDay;

    /// Server-authoritative clock (Stage 11). Wrapped into [0,1) because the packet counts ticks.
    public void SetTimeOfDay(float t) => mTimeOfDay = t - MathF.Floor(t);

    // Player
    private Vector3 mSpawnPos;

    // Movement intent waiting to be consumed by the next simulation tick, and whether this frame
    // sampled the keyboard at all (it doesn't while a screen is open - the player should stand
    // still and keep falling, not carry on walking from the last sample). See AccumulatePlayerInput.
    private PlayerInput mPendingInput;
    private bool mInputSampledThisFrame;

    // Rate limits for held mouse buttons, in seconds. Vanilla places a block every 4 ticks while
    // right-click is held and swings the arm on a 6-tick cycle; both used to fire once per frame,
    // which made building click-per-block and the swing animation framerate-dependent.
    private const float PLACE_INTERVAL = 4f / TickSystem.TPS;
    private const float SWING_INTERVAL = 6f / TickSystem.TPS;
    private float mPlaceCooldown;
    private float mSwingCooldown;
    private PlayerArm? mPlayerArm;
    public Player GetPlayer => mPlayer;

    // Structure export selection corners
    // F1/F2 set these two block-space corners in-game to mark a region for exporting to a
    // JSON structure file (see StructureLoader); null until the player has pressed F1/F2
    // for this selection.
    private Vector3i? mExportCorner1;
    private Vector3i? mExportCorner2;

    // Global Random
    private Random mGameRandom = new();
    public Random GameRandom => mGameRandom;

    // ASCII post-processing effect: renders the normal scene to an off-screen framebuffer,
    // then a fullscreen-quad shader pass samples it and maps each cell to an ASCII glyph
    // from mAsciiAtlas, producing a text-mode-style visual filter when AsciiEnabled is true.
    private AsciiFramebuffer mAsciiFbo = null!;
    private Shader mAsciiShader = null!;
    private FullscreenQuad mFsQuad = null!;
    private uint mAsciiAtlas; // GL texture handle for the glyph atlas used by the ASCII shader
    private int mAsciiCharCount; // number of glyphs in mAsciiAtlas, used by the shader for lookup
    public bool AsciiEnabled { get; set; } = false;

    /// <summary>
    /// Applies a new UI scale live (options menu "Screen Scaling" setting) - updates
    /// UIHelper.UI_SCALE (read by the custom-drawn hotbar/HUD/inventory screens on their next
    /// draw) and tells the ImGui controller to rebuild its fonts/style at the new scale (native
    /// menu widgets don't re-read UI_SCALE on their own since they're baked in at font/style
    /// build time).
    /// </summary>
    public void SetUiScale(float uiScale)
    {
        UIHelper.UI_SCALE = uiScale;
        mImGuiController?.ApplyUiScale(uiScale);
    }

    // Game State
    public GameState CurrentState { get; private set; }

    /// <summary>
    /// Samples the keyboard into <see cref="mPendingInput"/> for the next tick to consume.
    ///
    /// Jump is sampled twice on purpose: held (IsKeyDown) for rising while flying or swimming,
    /// and edge-triggered (IsKeyPressed) for ground jumps, which must not repeat while the key
    /// is held. ToggleFly is edge-triggered for the same reason.
    ///
    /// The edge-triggered flags are OR-ed in rather than assigned, because frames and ticks no
    /// longer line up: a frame can produce zero ticks (at high framerates, most do), and a jump
    /// pressed and released across two such frames would otherwise never reach the simulation.
    /// They latch here until a tick actually consumes them.
    /// </summary>
    private void AccumulatePlayerInput()
    {
        mPendingInput.MoveForward = IsKeyDown(Keybindings.MoveForward);
        mPendingInput.MoveBack    = IsKeyDown(Keybindings.MoveBack);
        mPendingInput.MoveLeft    = IsKeyDown(Keybindings.MoveLeft);
        mPendingInput.MoveRight   = IsKeyDown(Keybindings.MoveRight);
        mPendingInput.Jump        = IsKeyDown(Keybindings.Jump);
        mPendingInput.Sprint      = IsKeyDown(Keybindings.Sprint);
        // One key for both: sneaking on the ground, descending while flying - as in Minecraft.
        mPendingInput.Sneak       = IsKeyDown(Keybindings.Sneak);
        mPendingInput.Descend     = IsKeyDown(Keybindings.Sneak);

        mPendingInput.JumpPressed |= IsKeyPressed(Keybindings.Jump);
        mPendingInput.ToggleFly   |= IsKeyPressed(Keybindings.ToggleFly);

        mInputSampledThisFrame = true;
    }

    #region IGameContext

    // Explicit implementations: shared code in Common sees the narrow interfaces, while client
    // code keeps calling Game.Instance.AudioManager / .ParticleSystem / .Hotbar and getting the
    // concrete types back, with their full APIs. Nothing in the client had to change.
    IAudioManager IGameContext.AudioManager => mAudioManager;
    IParticleSpawner IGameContext.ParticleSystem => ParticleSystem;
    IHotbar? IGameContext.Hotbar => mHotbar;

    #endregion

    #endregion

    // Input helpers
    // IsKeyPressed = true for exactly one frame, the frame the key was first pushed down (good
    // for one-shot actions like opening inventory). IsKeyDown = true every frame the key is held
    // (good for continuous actions like walking).
    public bool IsKeyPressed(SilkKey key) => mPressedThisFrame.Contains(key);
    public bool IsKeyDown(SilkKey key) => mKeyboard?.IsKeyPressed(key) ?? false;
    public bool IsMouseButtonPressed(SilkMouseButton button) => mMouseButtonsPressed.Contains(button);
    public bool IsMouseButtonDown(SilkMouseButton button) => mMouse?.IsButtonPressed(button) ?? false;

    /// <summary>
    /// Creates the Silk.NET window (OpenGL 3.3 Core, no VSync) and wires its lifecycle
    /// events to this instance's handler methods. Does not create a GL context or start
    /// the message loop yet — that happens lazily when <see cref="Run"/> is called and
    /// Silk.NET fires <c>Load</c>.
    /// </summary>
    public Game(int width, int height, string title)
    {
        mGameRandom = new Random();
        mAudioManager = new AudioManager();

        var options = WindowOptions.Default;
        options.Size = new Silk.NET.Maths.Vector2D<int>(width, height);
        options.Title = title;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));
        options.VSync = true;

        // Create the window, then hook up our methods to Silk.NET's lifecycle events. None of
        // these run yet - Run() below is what actually starts the window's main loop.
        mWindow = Window.Create(options);
        mWindow.Load += OnLoad;
        mWindow.Update += OnUpdateFrame;
        mWindow.Render += OnRenderFrame;
        mWindow.Resize += OnResize;
        mWindow.Closing += OnUnload;
    }

    /// <summary>
    /// Blocks the calling thread and runs Silk.NET's main window loop (which internally
    /// pumps OnLoad, then repeatedly Update/Render, until the window is closed), then
    /// disposes the window. This is the entry point called from Program.Main.
    /// </summary>
    public void Run()
    {
        mWindow.Run();
        mWindow.Dispose();
    }

    /// <summary>Requests the window close, which will end the main loop in <see cref="Run"/>.</summary>
    public void Close() => mWindow.Close();

    #region Life

    // Runs once, after the window and GL context exist. Everything that needs a graphics
    // context (shaders, textures, the world, UI screens) gets created here.
    private void OnLoad()
    {
        Instance = this;

        GlContext.Gl = GL.GetApi(mWindow);

        // Bind the two seams shared code reaches through, before anything in Common runs.
        // GameContext.Current throws if it's unbound, and RenderBackend silently draws nothing,
        // so getting these in early - right after the GL context exists - matters.
        GameContext.Bind(this);
        RenderBackend.Bind(GlRenderBackend.Active);

        // Must run before any UI class (Hotbar, HudScreen, InventoryScreenBase-derived screens,
        // ImGuiController) is constructed, since their pixel-size fields are computed from
        // UIHelper.UI_SCALE at that point.
        UIHelper.InitScale(mWindow.Size.Y);

        // Set up input
        mInputContext = mWindow.CreateInput();
        mKeyboard = mInputContext.Keyboards.Count > 0 ? mInputContext.Keyboards[0] : throw new Exception("No keyboard found");
        mMouse = mInputContext.Mice.Count > 0 ? mInputContext.Mice[0] : throw new Exception("No mouse found");

        // Silk.NET only tells us "key went down" as an event, not "is it down right now" per
        // frame, so we record it here and clear the set at the end of every Update.
        mKeyboard.KeyDown += (kb, key, scancode) => mPressedThisFrame.Add(key);
        mKeyboard.KeyChar += (kb, c) => mImGuiController?.PressChar(c);
        mMouse.MouseDown += (mouse, button) => mMouseButtonsPressed.Add(button);
        mMouse.MouseMove += (mouse, pos) =>
        {
            mMousePosition = pos;
        };
        mMouse.Scroll += (mouse, wheel) =>
        {
            if (CurrentState == GameState.Playing && mHotbar != null)
            {
                mHotbar.ScrollSlot(-(int)wheel.Y);
                var block = mHotbar.GetSelectedBlock();
                if (block.HasValue)
                    mPlayer.SelectedBlock = block.Value;
            }

            mImGuiController?.MouseScroll(new Vector2(wheel.X, wheel.Y));
        };

        InitGl();
        LoadResources();
        InitUi();
        Keybindings.Load();

        CurrentState = GameState.MainMenu;
        mMouse.Cursor.CursorMode = CursorMode.Normal;

        mAsciiFbo = new AsciiFramebuffer(mWindow.Size.X, mWindow.Size.Y);
        mAsciiShader = new Shader(File.ReadAllText("Shaders/ascii.vert"), File.ReadAllText("Shaders/ascii.frag"));
        mFsQuad = new FullscreenQuad();
        (mAsciiAtlas, mAsciiCharCount) = LoadAsciiAtlas();
    }

    // Fires whenever the window is resized. Keeps the GL viewport, the player camera's
    // aspect ratio, the ASCII post-process framebuffer, and ImGui all in sync with the new
    // window dimensions.
    private void OnResize(Silk.NET.Maths.Vector2D<int> size)
    {
        GlContext.Gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);

        if (mPlayer != null)
            mPlayer.Camera.AspectRatio = size.X / (float)size.Y;

        if (mAsciiFbo != null)
        {
            mAsciiFbo.Dispose();
            mAsciiFbo = new AsciiFramebuffer(size.X, size.Y);
        }

        mImGuiController?.WindowResized(size.X, size.Y);
    }

    // Guards against double-disposal: Silk.NET's Closing event and an explicit Dispose()
    // call could both reach OnUnload, so this flag makes the cleanup idempotent.
    private bool mUnloaded;

    // Frees all GL-backed and unmanaged resources. Runs on window close (via mWindow.Closing)
    // and/or explicit Dispose(); guarded by mUnloaded so it only ever executes once.
    private void OnUnload()
    {
        if (mUnloaded) return;
        mUnloaded = true;

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
        // mInputContext is owned and disposed by Silk.NET when the window closes
    }

    public void Dispose()
    {
        OnUnload();
        mWindow?.Dispose();
    }

    #endregion

    #region Inits

    // One-time GL state setup: background clear color (sky blue, used before the sky
    // shader/dome takes over), depth testing for correct 3D occlusion, and backface culling
    // with counter-clockwise winding as the "front" face convention used by all mesh builders.
    private void InitGl()
    {
        var gl = GlContext.Gl;

        // Diagnostic: if DepthBits prints 0, depth testing below is silently a no-op - every fragment passes regardless of distance, and later-drawn geometry just overwrites earlier geometry, which looks like solid blocks being see-through.
        unsafe
        {
            string glVersion = gl.GetStringS(StringName.Version);
            string glRenderer = gl.GetStringS(StringName.Renderer);
            string glVendor = gl.GetStringS(StringName.Vendor);

            // GL_DEPTH_BITS/GL_STENCIL_BITS were removed from the core profile in GL 3.2+, so query the default framebuffer's depth/stencil attachment sizes directly instead.
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, GLEnum.Depth,
                FramebufferAttachmentParameterName.FramebufferAttachmentDepthSize, out int depthBits);
            gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, GLEnum.Stencil,
                FramebufferAttachmentParameterName.FramebufferAttachmentStencilSize, out int stencilBits);

            Console.WriteLine($"[GL] Vendor: {glVendor}");
            Console.WriteLine($"[GL] Renderer: {glRenderer}");
            Console.WriteLine($"[GL] Version: {glVersion}");
            Console.WriteLine($"[GL] DepthBits: {depthBits}, StencilBits: {stencilBits}");
        }

        gl.ClearColor(0.5f, 0.7f, 1.0f, 1.0f);
        gl.Enable(EnableCap.DepthTest);
        gl.Enable(EnableCap.CullFace);
        gl.CullFace(TriangleFace.Back);
        gl.FrontFace(FrontFaceDirection.Ccw);
    }

    // Loads all shaders/textures/subsystems that don't depend on a specific world/save
    // (as opposed to InitWorld, which is per-save). Called once from OnLoad.
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

        // Hand the atlases to the render backend. Entity.SharedWorldTexture is gone: a GL
        // texture can't be a static on a Common type any more.
        GlRenderBackend.Active.SetAtlases(WorldTexture, ItemTexture);
        Entity.SharedRandom = mGameRandom;
        Entity.PlayStepSoundCallback = mAudioManager.PlayBlockContactSound;
    }

    // Loads or creates the current save (Serialization.WorldName), constructs the World,
    // Player, inventory, and dependent systems, and restores saved state (position, health,
    // inventory, paintings, entities, block entities). Called when transitioning from
    // MainMenu/Loading into Playing. Structures are no longer stamped in here - they come out of
    // the chunk generator now, so they exist everywhere rather than in one patch around spawn.
    private void InitWorld()
    {
        var worldData = Serialization.LoadWorldData(Serialization.WorldName)
                        ?? Serialization.CreateWorld(
                            Serialization.WorldName,
                            customSeed: null,
                            worldTheme: (int)mWorldGenSettings.Theme
                        );

        IsCreative = worldData.IsCreative;
        this.mTimeOfDay = worldData.WorldTime;

        // Normalize the loaded time-of-day back into [0,1) in case it was saved slightly
        // out of range (e.g. from floating point drift over a long play session).
        mTimeOfDay -= MathF.Floor(mTimeOfDay);
        if (mTimeOfDay < 0f)
            mTimeOfDay += 1f;

        mWorldGenSettings = WorldGenSettings.Build(worldData.WorldTheme);

        mRenderer.ResetCloudOffset();

        mWorld = new World(worldData.Seed, mWorldGenSettings);

        // Terrain now streams in asynchronously, so the spawn column is empty at this point.
        // Block until it exists, or FindSpawnPosition hits its "nothing here" fallback and drops
        // the player through the world.
        Vector3 spawnColumn = new(0, 0, 0);
        mWorld.PrimeAround(spawnColumn, radiusChunks: 2);
        mSpawnPos = mWorld.FindSpawnPosition(0, 0);

        Vector3 playerPos = mSpawnPos;
        if (worldData.HasPlayerPosition)
        {
            playerPos = new Vector3(worldData.PlayerX, worldData.PlayerY, worldData.PlayerZ);
            // Returning to a saved position: stream that area in too, or the player falls before
            // the ground under them arrives.
            mWorld.PrimeAround(playerPos, radiusChunks: 2);
        }

        mPlayer = new Player(playerPos, mWindow.Size.X / (float)mWindow.Size.Y);

        // Singleplayer is the one-player case of the general list. Registering here keeps
        // World.Players truthful, so code written against it works unchanged under a server.
        mWorld.Players.Clear();
        mWorld.Players.Add(mPlayer);

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
    }

    // Creates the ImGuiController and every UI screen instance up front (screens are cheap
    // to keep alive and swap visibility on rather than construct/destroy per state change),
    // then wires the few cross-screen callbacks (pause menu buttons, death screen, main menu).
    private void InitUi()
    {
        mImGuiController = new ImGuiController(mWindow.Size.X, mWindow.Size.Y);
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
        mChatScreen = new ChatScreen();
        mConnectingScreen = new ConnectingScreen();
        mDisconnectedScreen = new DisconnectedScreen();

        mPauseScreen.OnPauseQuitGame += ReturnToMainMenu;
        mPauseScreen.OnResumeGame += ResumeGame;
        mPauseScreen.OnSaveGame += SaveGame;

        mDeathScreen.OnReturnToMainMenu += ReturnToMainMenuNoSave;
        mDeathScreen.OnRespawn += RequestRespawn;

        mMainMenuScreen.OnTitleQuitGame += Close;
        mMainMenuScreen.OnStartGame += StartGame;
        mMainMenuScreen.OnJoinServer += JoinServer;

        // Cancelling a connect and dismissing a kick do the same two things.
        mChatScreen.OnSendMessage += SendChatMessage;
        mConnectingScreen.OnCancel += LeaveServer;
        mDisconnectedScreen.OnBackToMenu += LeaveServer;
    }

    #endregion

    #region Update

    // Runs once per frame. CurrentState decides which "screen" is active - most non-Playing
    // states are just an inventory-like screen layered on top of the normal game (world keeps
    // ticking via UpdateGameLogic, but Escape/inventory key closes the screen instead of opening
    // the pause menu).
    private void OnUpdateFrame(double deltaTime)
    {
        float dt = (float)deltaTime;
        mImGuiController.Update(dt, mMouse, mKeyboard);

        // Drives the play/silence/play cycle; outside any state, so the gap keeps counting down
        // while paused or in a menu rather than stalling there.
        mAudioManager.TickMusic(dt);

        // Before the switch, so a kick changes CurrentState before this frame picks what to draw.
        PumpNetwork();

        switch (CurrentState)
        {
            case GameState.MainMenu:
                mMainMenuScreen?.Render();
                break;

            case GameState.Loading:
                mLoadingScreen?.Render();
                mLoadingFrames++;

                // Wait a few frames before calling the (synchronous, blocking) InitWorld so
                // the loading screen actually gets a chance to be drawn and presented to the
                // player first; otherwise the world generation would run within the same
                // frame the state changed to Loading and the screen would never be seen.
                if (mLoadingFrames >= 3)
                {
                    InitWorld();
                    CurrentState = GameState.Playing;
                    SetCursorGrabbed(true);
                }
                break;

            case GameState.Paused:
                if (IsKeyPressed(SilkKey.Escape))
                {
                    ResumeGame();
                    break;
                }

                // Pausing only stops the world in singleplayer. On a server the world keeps going
                // whether this client is looking at a menu or not, so keep simulating - otherwise
                // entities freeze, chunks stop arriving, and everything snaps on resume.
                if (mNetwork != null)
                {
                    UpdateGameLogic(dt);
                    if (CurrentState == GameState.Died) break;
                }

                mPauseScreen?.Render();
                break;

            case GameState.Died:
                mDeathScreen?.Render();
                break;

            case GameState.Connecting:
                PumpArrivingChunks();
                mConnectingScreen?.Render();
                break;

            case GameState.Disconnected:
                mDisconnectedScreen?.Render();
                break;

            case GameState.Inventory:
                if (IsKeyPressed(SilkKey.Escape) || IsKeyPressed(Keybindings.Inventory))
                {
                    CloseInventory();
                    break;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) break;
                if (IsCreative)
                    mCreativeInventoryScreen?.Render();
                else
                    mInventoryScreen?.Render();
                break;

            case GameState.Crafting:
                if (IsKeyPressed(SilkKey.Escape) || IsKeyPressed(Keybindings.Inventory))
                {
                    CloseCrafting();
                    break;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) break;
                mCraftingScreen?.Render();
                mHotbar?.Render();
                break;

            case GameState.Furnace:
                if (IsKeyPressed(SilkKey.Escape) || IsKeyPressed(Keybindings.Inventory))
                {
                    CloseFurnace();
                    break;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) break;
                mFurnaceScreen?.Render();
                mHotbar?.Render();
                break;

            case GameState.Chest:
                if (IsKeyPressed(SilkKey.Escape) || IsKeyPressed(Keybindings.Inventory))
                {
                    CloseChest();
                    break;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) break;
                mChestScreen?.Render();
                mHotbar?.Render();
                break;

            case GameState.DoubleChest:
                if (IsKeyPressed(SilkKey.Escape) || IsKeyPressed(Keybindings.Inventory))
                {
                    CloseDoubleChest();
                    break;
                }

                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) break;
                mDoubleChestScreen?.Render();
                mHotbar?.Render();
                break;

            case GameState.Playing:
                // Chat takes the keyboard first: Escape must close the input, not pause the game,
                // and typing "s" must not walk you backwards.
                if (mChatScreen.IsOpen)
                {
                    UpdateGameLogic(dt);
                    if (CurrentState == GameState.Died) break;

                    RenderMultiplayerOverlays(dt);
                    mHotbar?.Render();
                    mRenderer.RenderHudOverlay();
                    break;
                }

                if (IsKeyPressed(SilkKey.Escape))
                {
                    PauseGame();
                    break;
                }

                if (IsKeyPressed(Keybindings.Inventory))
                {
                    OpenInventory();
                    break;
                }

                if (mNetwork != null)
                {
                    if (IsKeyPressed(SilkKey.T)) mChatScreen.Open(withSlash: false);
                    if (IsKeyPressed(SilkKey.Slash)) mChatScreen.Open(withSlash: true);
                }

                ProcessInput(dt);
                UpdateGameLogic(dt);
                if (CurrentState == GameState.Died) break;

                RenderMultiplayerOverlays(dt);
                mHotbar?.Render();
                mRenderer.RenderHudOverlay();
                break;
        }

        // Clear per-frame input state
        mPressedThisFrame.Clear();
        mMouseButtonsPressed.Clear();
        mInputSampledThisFrame = false;
    }

    // Handles all per-frame input for the Playing state: debug toggles/hotkeys, mouse-look,
    // hotbar slot selection, item dropping, and player/attack/interaction updates. Only
    // called while GameState.Playing is active (other states have their own more limited
    // Escape/inventory-key handling inline in OnUpdateFrame).
    private void ProcessInput(float dt)
    {
        mPlayer.SelectedBlock = mHotbar?.GetSelectedBlock() ?? BlockType.Air;

        if (IsKeyPressed(Keybindings.Wireframe))
        {
            mWireframeMode = !mWireframeMode;
            // NOTE: ImGuiController unconditionally resets GL PolygonMode to Fill every
            // frame (see its Render/NewFrame code) without restoring whatever mode was
            // active before it ran. Since ImGui renders after the 3D world each frame,
            // this stomps the wireframe toggle here almost immediately, silently breaking
            // the X-key wireframe view. Known issue, not fixed here.
            GlContext.Gl.PolygonMode(TriangleFace.FrontAndBack, mWireframeMode ? PolygonMode.Line : PolygonMode.Fill);
        }

        // Debug keys are singleplayer only: teleporting and the clock are the server's business
        // there, and mobs spawned here would exist for nobody else.
        if (mNetwork == null)
        {
            if (IsKeyPressed(Keybindings.ResetPosition))
                mPlayer.ResetPosition();

            if (IsKeyPressed(SilkKey.F9))
                mTimeOfDay = 0.75f;

            if (IsKeyPressed(SilkKey.F10))
            {
                int spawned = mMobSpawner.DebugSpawnHostilesNow(candidateCount: 2000, ignoreCap: true);
                mWindow.Title = $"Spawn test: spawned {spawned} hostiles (F9=midnight, F10=burst)";
            }
        }

        if (IsKeyPressed(Keybindings.ToggleCursor))
        {
            mCursorGrabbed = !mCursorGrabbed;
            SetCursorGrabbed(mCursorGrabbed);
            mFirstMouse = true;
        }

        if (IsKeyPressed(Keybindings.Screenshot))
            TakeIsoScreenshot();

        // F1/F2: structure export tool. F1 marks the first corner of a box under the
        // crosshair, F2 marks the second and immediately serializes every block within the
        // resulting box to a new JSON structure file via StructureLoader.Export.
        if (IsKeyPressed(SilkKey.F1))
        {
            var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);
            if (hit.Type == RaycastHitType.Block)
            {
                mExportCorner1 = hit.BlockPos;
                mWindow.Title = $"Export: Corner 1 set to ({hit.BlockPos.X}, {hit.BlockPos.Y}, {hit.BlockPos.Z})";
            }
        }

        if (IsKeyPressed(SilkKey.F2))
        {
            var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);
            if (hit.Type == RaycastHitType.Block)
            {
                mExportCorner2 = hit.BlockPos;
                if (mExportCorner1.HasValue)
                {
                    string file = StructureLoader.Export(mWorld, mExportCorner1.Value, mExportCorner2.Value);
                    mWindow.Title = $"Export: Saved to {file}";
                    mExportCorner1 = null;
                    mExportCorner2 = null;
                }
                else
                {
                    mWindow.Title = "Export: Corner 2 set, but corner 1 not set. Press F1 first.";
                }
            }
        }

        if (IsKeyPressed(Keybindings.RenderDistUp))
            mPlayer.Camera.RenderDistance = MathF.Min(mPlayer.Camera.RenderDistance + 32f, 512f);

        if (IsKeyPressed(Keybindings.RenderDistDown))
            mPlayer.Camera.RenderDistance = MathF.Max(mPlayer.Camera.RenderDistance - 32f, 32f);

        if (mCursorGrabbed)
        {
            // On the first frame after grabbing the cursor (or right after startup), there's
            // no previous mouse position to diff against, which would otherwise produce a
            // huge spurious look-delta jump. Snap mLastMousePos to the current position once
            // instead so the first frame's delta is zero.
            if (mFirstMouse)
            {
                mLastMousePos = mMousePosition;
                mFirstMouse = false;
            }

            var delta = mMousePosition - mLastMousePos;
            mPlayer.HandleMouseLook(new Vector2(delta.X, delta.Y));
            mLastMousePos = mMousePosition;
        }

        if (IsKeyPressed(SilkKey.Number1)) SelectHotbarSlot(0);
        if (IsKeyPressed(SilkKey.Number2)) SelectHotbarSlot(1);
        if (IsKeyPressed(SilkKey.Number3)) SelectHotbarSlot(2);
        if (IsKeyPressed(SilkKey.Number4)) SelectHotbarSlot(3);
        if (IsKeyPressed(SilkKey.Number5)) SelectHotbarSlot(4);
        if (IsKeyPressed(SilkKey.Number6)) SelectHotbarSlot(5);
        if (IsKeyPressed(SilkKey.Number7)) SelectHotbarSlot(6);
        if (IsKeyPressed(SilkKey.Number8)) SelectHotbarSlot(7);
        if (IsKeyPressed(SilkKey.Number9)) SelectHotbarSlot(8);
        if (IsKeyPressed(SilkKey.Number0)) SelectHotbarSlot(9);

        if (IsKeyPressed(Keybindings.DropItem))
        {
            // On a server this is a request: it owns the inventory and the entity that comes out
            // of it, so dropping locally would spawn an item only this client can see.
            if (mNetwork != null)
            {
                SendDropHeld();
            }
            else if (mHotbar.GetSelectedStack() is { } selected)
            {
                mInventory.ConsumeOne(PlayerInventory.HOTBAR_START + mHotbar.SelectedSlotIndex);
                if (!mHotbar.GetSelectedStack().HasValue)
                    mPlayer.SelectedBlock = BlockType.Air;

                var thrown = selected.WithCount(1);
                var spawnPos = mPlayer.Camera.Position + mPlayer.Camera.Front * 0.5f;
                var vel = mPlayer.Camera.Front * 5f + new Vector3(0f, 2f, 0f);
                var drop = new DroppedItemEntity(spawnPos, thrown);
                drop.Velocity = vel;
                mWorld.AddEntity(drop);
            }
        }

        // Player no longer reads the keyboard itself - it consumes intent, so the same movement
        // code can be driven by packets on a server. Translating keys to intent is the host's job,
        // and this is the only place in the client that does it. The player itself is moved by
        // UpdateGameLogic's fixed-tick loop, not here.
        AccumulatePlayerInput();

        mPlayerArm?.Update(dt, mPlayer.HorizontalSpeed);

        if (mPlaceCooldown > 0f) mPlaceCooldown -= dt;
        if (mSwingCooldown > 0f) mSwingCooldown -= dt;

        bool holdingAttack = IsMouseButtonDown(SilkMouseButton.Left) && mCursorGrabbed;
        mPlayer.UpdateBreaking(mWorld, dt, holdingAttack);

        // A held mouse button re-swings on a fixed cadence rather than every frame, which is both
        // what the animation is timed for and what stops a swing packet going out per frame.
        if (holdingAttack && mSwingCooldown <= 0f)
        {
            mPlayerArm?.TriggerSwing();
            SendSwing();
            mSwingCooldown = SWING_INTERVAL;
        }
        else if (!holdingAttack)
        {
            mSwingCooldown = 0f;
        }

        if (IsMouseButtonPressed(SilkMouseButton.Left))
        {
            var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);
            if (hit.Type == RaycastHitType.Entity)
            {
                // On a server the hit is a request: it checks reach, works out the damage from the
                // held slot it already knows about, and answers with health and knockback.
                if (mNetwork != null)
                {
                    SendAttack(hit.Entity!.Id);
                }
                else
                {
                    // Damage comes from whatever is held: every tool defines AttackDamage per tier
                    // (see ItemSword/ItemAxe/etc). Blocks and an empty hand fall back to
                    // BARE_HAND_DAMAGE, matching Item.AttackDamage's default for non-weapon items.
                    var held = mHotbar?.GetSelectedStack();
                    int damage = held is { IsBlock: false } heldItem
                        ? ItemRegistry.Get(heldItem.Item).AttackDamage
                        : BARE_HAND_DAMAGE;

                    hit.Entity!.TakeDamage(damage);

                    var knockDir = hit.Entity.Position - mPlayer.Position;
                    knockDir.Y = 0;
                    if (knockDir.LengthSquared() > 0.001f)
                        knockDir = Vector3.Normalize(knockDir);
                    hit.Entity.Velocity += new Vector3(knockDir.X, 0.7f, knockDir.Z) * 14f;
                }
            }
        }

        // Holding right-click lays a run of blocks instead of one per click. The first block goes
        // down on the press itself; the rest follow at the vanilla 4-tick interval.
        bool usePressed = IsMouseButtonPressed(SilkMouseButton.Right);
        bool useHeld = IsMouseButtonDown(SilkMouseButton.Right) && mCursorGrabbed;

        if (!useHeld)
            mPlaceCooldown = 0f;

        if (usePressed || (useHeld && mPlaceCooldown <= 0f))
        {
            mPlaceCooldown = PLACE_INTERVAL;
            mPlayerArm?.TriggerSwing();
            SendSwing();
            var selected = mHotbar.GetSelectedStack();
            mPlayer.HandleBlockInteraction(mWorld, false, true);

            if (selected is not { IsBlock: false })
                return;

            // Buckets, food, bows, paintings: the effect is a world or inventory change, both of
            // which the server owns. Running it locally would eat the item twice and spawn things
            // only this client can see.
            if (mNetwork != null)
                SendUseItem();
            else
                mPlayer.UseHeldItem(mWorld, selected.Value.Item);
        }
    }

    // F7: renders an off-screen isometric snapshot of the world (see IsoScreenshot) and
    // saves it to the user's Documents folder, independent of the normal first-person view.
    private void TakeIsoScreenshot()
    {
        if (mWorld == null || mPlayer == null)
            return;

        mWindow.Title = "Taking isometric screenshot...";
        var shooter = new IsoScreenshot(mWorld, mTimeOfDay);
        shooter.Capture();
        mWindow.Title = "Screenshot saved! (check Documents folder)";
    }

    private void SelectHotbarSlot(int slot)
    {
        mHotbar.SetHotbarSlot(slot);
        var block = mHotbar.GetSelectedBlock();
        if (block.HasValue)
            mPlayer.SelectedBlock = block.Value;
    }

    // Runs the actual simulation: day/night, entities, and however many fixed-length "ticks" have
    // built up since last frame (see TickSystem - this decouples game logic speed from framerate).
    private void UpdateGameLogic(float dt)
    {
        Entity.ListenerPosition = mPlayer.Position;
        Entity.SfxVol = mAudioManager.SfxVol;

        // With a screen open ProcessInput never runs, so nothing sampled the keyboard this frame.
        // The player still has to tick (opening the inventory mid-fall must not leave you hanging
        // in the air) - just with no movement intent.
        if (!mInputSampledThisFrame)
            mPendingInput = default;

        // Advances continuously with real elapsed time, not per-tick, so it stays smooth regardless
        // of the fixed-tick loop below. Also runs when connected - TimeUpdate (every 5s) only
        // corrects drift now, since relying on it alone froze the clock between packets.
        mTimeOfDay += dt / DAY_LENGTH;
        if (mTimeOfDay >= 1f)
            mTimeOfDay -= 1f;

        // Everything in this loop runs at a fixed 20 Hz regardless of framerate (see
        // TickSystem). Order matters: entities tick before mob spawning (so newly-dead
        // entities are removed before spawn caps are counted), chunk streaming happens before
        // scheduled/random ticks (so those iterate this tick's resident set), and furnace
        // ticking is last so it reflects this tick's world state.
        int ticks = mTickSystem.Accumulate(dt);
        for (int i = 0; i < ticks; i++)
        {
            mTickCount++;

            // The player now simulates on the same 20 Hz clock as everything else. Every friction
            // and gravity constant in Player is a per-tick multiplier, so this is the only rate at
            // which they mean what they're supposed to.
            mPlayer.Input = mPendingInput;
            mPlayer.Tick(mWorld);

            // The latched edges have been consumed; clear them so one press is one jump.
            mPendingInput.JumpPressed = false;
            mPendingInput.ToggleFly = false;

            mWorld.TickEntities();

            // The server owns spawning; running this too would fill the world with mobs only
            // this client can see.
            if (mNetwork == null)
                mMobSpawner.Tick();
            mWorld.TickStreaming(mPlayer.Position);
            SendLocalPosition();
            SendHeldSlotIfChanged();
            mWorld.RandomDisplayUpdates(mPlayer.Position);

            // Fluids, gravity, growth and decay are the server's simulation; it sends the results
            // as block changes. Running them here too would flow water the server never flowed.
            if (mNetwork == null)
            {
                mWorld.DoScheduledTick();
                mWorld.DoRandomTick();
            }
            mRenderer.TickClouds();

            if (mNetwork == null)
                BlockEntityManager.TickFurnaces();
        }

        // Light and mesh rebuilds are presentation, not simulation, so they run every frame. Inside
        // the loop above they inherited the tick's 50 ms granularity, and on a frame that produced
        // no tick at all a block you had just placed simply wasn't drawn yet.
        mWorld.Update(mPlayer.Position);

        // Camera placement, view bob and the FOV kick are presentation, so they run every frame and
        // interpolate between the last two ticks - that partial-tick lerp is what lets a 20 Hz
        // simulation still look smooth at 144 fps.
        mPlayer.UpdateVisual(dt, mTickSystem.GetPartialTick());

        ParticleSystem.Update(dt, mWorld);
        ParticleSystem.UpdateSmoke(dt, mWorld);

        if (mPlayer.Health <= 0 && CurrentState != GameState.Died)
        {
            CurrentState = GameState.Died;
            SetCursorGrabbed(false);
        }
    }

    #endregion

    #region Render

    // Runs once per frame after Update. Draws the 3D world (optionally through the ASCII
    // post-processing pass) and then the ImGui-based UI on top.
    private void OnRenderFrame(double deltaTime)
    {
        var gl = GlContext.Gl;

        if (mWorld != null && mPlayer != null)
        {
            UpdateTitle(deltaTime);

            // ASCII mode: render the normal scene into an off-screen framebuffer first, then draw
            // a fullscreen quad that turns it into colored ASCII characters via a shader.
            if (AsciiEnabled)
            {
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, mAsciiFbo.Fbo);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                mRenderer.RenderFrame(mTimeOfDay, mWorldGenSettings);

                gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                gl.Clear(ClearBufferMask.ColorBufferBit);
                gl.Disable(EnableCap.DepthTest);

                mAsciiShader.Use();
                mAsciiShader.SetVector2("uScreenSize", new Vector2(mWindow.Size.X, mWindow.Size.Y));
                mAsciiShader.SetVector2("uCharSize", new Vector2(8, 8));
                mAsciiShader.SetInt("uCharCount", mAsciiCharCount);

                gl.ActiveTexture(TextureUnit.Texture0);
                gl.BindTexture(TextureTarget.Texture2D, mAsciiFbo.ColorTexture);
                mAsciiShader.SetInt("uScene", 0);

                gl.ActiveTexture(TextureUnit.Texture1);
                gl.BindTexture(TextureTarget.Texture2D, mAsciiAtlas);
                mAsciiShader.SetInt("uAsciiAtlas", 1);

                mFsQuad.Draw();
                gl.Enable(EnableCap.DepthTest);
            }
            else
            {
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                mRenderer.RenderFrame(mTimeOfDay, mWorldGenSettings);
            }
        }
        else
        {
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        mImGuiController?.Render();
        // Silk.NET swaps buffers automatically
    }

    // Rebuilds the window title once per second with FPS/TPS/position/mode/time debug info
    // (the game has no persistent on-screen debug overlay, so the title bar serves as one).
    // hours24 remaps mTimeOfDay (0=dawn) to a 24-hour clock where dawn reads as 06:00.
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

            float hours24 = (mTimeOfDay * 24f + 6f) % 24f;
            int h = (int)hours24;
            int m = (int)((hours24 - h) * 60);
            string timeStr = $"{h:D2}:{m:D2}";

            mWindow.Title =
                $"DuncanCraft 2000 InDev | FPS:{mFrameCount} TPS:{mTickCount} | {mPlayer.SelectedBlock} | {pos.X:F1},{pos.Y:F1},{pos.Z:F1} Chunk:{chunkX},{chunkZ} [{mode}] Render:{renderDist} Time:{timeStr}";

            mFrameCount = 0;
            mTickCount = 0;
            mFpsTimer = 0;
        }
    }

    // Builds the GL texture atlas used by the ASCII post-processing shader (see
    // AsciiEnabled/OnRenderFrame). Each character glyph is hand-encoded as a 64-bit bitmask
    // representing an 8x8 pixel grid (1 bit per pixel, row-major), baked into `glyphs`
    // below; this method rasterizes those bitmasks into actual texture pixels and uploads
    // them as a horizontal strip atlas (one 8x8 glyph per column), in the same left-to-right
    // order as `chars`, so the shader can look up a glyph by its brightness-sorted index.
    private (uint texId, int charCount) LoadAsciiAtlas()
    {
        string chars = " .'`^\",:;il!i+_-?|/\\(){}[]<>tfjrxnuvczXYUJCLQ0OZmwqpdbkhaoegsyISTADEFGHKNPRVegsyIo*#MW&8%B@$23456791█";
        int charW = 8, charH = 8;

        // Each entry maps a character to its 8x8 monochrome glyph bitmap, packed as a
        // 64-bit integer (bit i = pixel (i%8, i/8), 1 = filled). Ordered/selected so that
        // `chars` above goes roughly from "visually sparse" to "visually dense" glyphs,
        // letting the ASCII shader approximate per-pixel brightness of the source scene by
        // picking a glyph whose ink density matches.
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
        byte[] pixels = new byte[width * charH * 4];

        for (int ci = 0; ci < chars.Length; ci++)
        {
            ulong glyph = glyphs.TryGetValue(chars[ci], out var g) ? g : 0UL;
            for (int row = 0; row < charH; row++)
            {
                // The 64-bit glyph is packed most-significant-byte-first as row 0, so shift
                // right by (56 - row*8) to bring the current row's 8 bits into the low byte.
                byte rowBits = (byte)((glyph >> (56 - row * 8)) & 0xFF);
                for (int col = 0; col < charW; col++)
                {
                    bool lit = (rowBits & (0x80 >> col)) != 0;
                    int idx = ((row * width) + (ci * charW + col)) * 4;
                    byte val = lit ? (byte)255 : (byte)0;
                    pixels[idx + 0] = val;
                    pixels[idx + 1] = val;
                    pixels[idx + 2] = val;
                    pixels[idx + 3] = val;
                }
            }
        }

        var gl = GlContext.Gl;
        uint texId = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texId);

        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
            (uint)width, (uint)charH, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        return (texId, chars.Length);
    }

    #endregion

    #region Cursor

    // Disabled cursor mode both hides the OS cursor and lets Silk.NET report unbounded
    // relative mouse motion (needed for FPS-style look), vs. Normal mode for UI screens
    // where the player needs a visible, unconstrained cursor to click widgets.
    private void SetCursorGrabbed(bool grabbed)
    {
        mCursorGrabbed = grabbed;
        if (mMouse != null)
            mMouse.Cursor.CursorMode = grabbed ? CursorMode.Disabled : CursorMode.Normal;
        if (grabbed)
            // Re-grabbing after being ungrabbed means the OS may have moved/reset the mouse
            // position outside our tracking, so force a re-sync next frame (see mFirstMouse
            // handling in ProcessInput) to avoid a big spurious look-delta jump.
            mFirstMouse = true;
    }

    #endregion

    // The Open*/Close* method pairs below (Inventory/Crafting/Furnace/Chest/DoubleChest)
    // all follow the same pattern: switch CurrentState, release the cursor so the player
    // can interact with ImGui widgets, and re-center the OS cursor so it doesn't reappear
    // wherever it happened to be during first-person mouse-look. Close* additionally lets
    // the screen commit any pending change (e.g. writing back inventory slot edits) via its
    // OnClose() before returning control to Playing.
    #region Game State

    private void StartGame(int volumeSFX, int volumeMusic, int worldTheme = 0, bool isCreative = false)
    {
        mWorldGenSettings = WorldGenSettings.Build(worldTheme);
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
        SetCursorGrabbed(false);
        mMouse.Position = new System.Numerics.Vector2(mWindow.Size.X / 2f, mWindow.Size.Y / 2f);
    }

    private void ResumeGame()
    {
        CurrentState = GameState.Playing;
        SetCursorGrabbed(true);
    }

    private void OpenInventory()
    {
        CurrentState = GameState.Inventory;
        SetCursorGrabbed(false);
        mMouse.Position = new System.Numerics.Vector2(mWindow.Size.X / 2f, mWindow.Size.Y / 2f);
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
        SetCursorGrabbed(true);
    }

    public void OpenCrafting()
    {
        CurrentState = GameState.Crafting;
        SetCursorGrabbed(false);
        mMouse.Position = new System.Numerics.Vector2(mWindow.Size.X / 2f, mWindow.Size.Y / 2f);
    }

    public void CloseCrafting()
    {
        mCraftingScreen?.OnClose();
        CurrentState = GameState.Playing;
        SetCursorGrabbed(true);
    }

    public void OpenFurnace(Vector3i pos)
    {
        var furnace = BlockEntityManager.GetOrCreateFurnace(pos);
        mFurnaceScreen.SetFurnace(furnace);
        CurrentState = GameState.Furnace;
        SetCursorGrabbed(false);
        mMouse.Position = new System.Numerics.Vector2(mWindow.Size.X / 2f, mWindow.Size.Y / 2f);
    }

    public void CloseFurnace()
    {
        mFurnaceScreen?.OnClose();
        CurrentState = GameState.Playing;
        SetCursorGrabbed(true);
    }

    public void OpenChest(Vector3i pos)
    {
        var chest = BlockEntityManager.GetOrCreateChest(pos);
        mChestScreen.SetChest(chest);
        CurrentState = GameState.Chest;
        SetCursorGrabbed(false);
        mMouse.Position = new System.Numerics.Vector2(mWindow.Size.X / 2f, mWindow.Size.Y / 2f);
    }

    public void CloseChest()
    {
        mChestScreen?.OnClose();
        CurrentState = GameState.Playing;
        SetCursorGrabbed(true);
    }

    public void OpenDoubleChest(Vector3i canonicalPos)
    {
        var chest = BlockEntityManager.GetOrCreateDoubleChest(canonicalPos);
        mDoubleChestScreen.SetChest(chest);
        CurrentState = GameState.DoubleChest;
        SetCursorGrabbed(false);
        mMouse.Position = new System.Numerics.Vector2(mWindow.Size.X / 2f, mWindow.Size.Y / 2f);
    }

    public void CloseDoubleChest()
    {
        mDoubleChestScreen?.OnClose();
        CurrentState = GameState.Playing;
        SetCursorGrabbed(true);
    }

    private List<SavedEntity> SaveWorldEntities() =>
        WorldEntitySerializer.Save(mWorld.Entities);

    private void LoadWorldEntities(List<SavedEntity> saved) =>
        WorldEntitySerializer.Load(saved, mWorld);

    // Writes the current save to disk: only chunks flagged HasChunkBeenModified are
    // rewritten (unmodified chunks can always be regenerated identically from the seed, so
    // skipping them significantly cuts save time/disk writes on large worlds), plus block
    // entities, player state, inventory, paintings, and world entities via XML metadata.
    private void SaveWorldToDisk()
    {
        // Only resident chunks - the streamer already saved any modified chunk it unloaded.
        int savedCount = 0;
        foreach (var chunk in mWorld.LoadedChunks)
        {
            if (chunk.HasChunkBeenModified)
            {
                Serialization.SaveChunk(chunk);
                savedCount++;
            }
        }

        Console.WriteLine($"Saved {savedCount} modified chunks.");

        BlockEntityManager.Save(Serialization.SaveLocation());

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

    // Releases the current world/player and all session-scoped renderer state, then
    // returns to the main menu. Shared by both the "save and quit" and "quit without
    // saving" (e.g. after death) flows below.
    private void TeardownWorld()
    {
        mRenderer.ClearSession();
        BlockEntityManager.Clear();
        mWorld?.Dispose();
        mWorld = null!;
        mPlayer = null!;

        CurrentState = GameState.MainMenu;
        SetCursorGrabbed(false);
        mMainMenuScreen.ResetToTitle();

        mAudioManager?.Stop();
    }

    private void ReturnToMainMenu()
    {
        // On a server the world isn't ours to save - drop the connection instead.
        if (IsMultiplayer)
        {
            LeaveServer();
            return;
        }

        SaveWorldToDisk();
        TeardownWorld();
    }

    private void ReturnToMainMenuNoSave() => TeardownWorld();

    #endregion

    #region Networking

    // `async void` is safe here: it's an event handler, and ConnectAsync reports failure as `false`
    // rather than throwing.
    private async void JoinServer(string address)
    {
        mConnectingScreen.ServerAddress = address;
        mConnectingScreen.CurrentPhase = ConnectingScreen.Phase.Connecting;
        CurrentState = GameState.Connecting;

        var network = new ClientNetwork();
        mConnecting = network;
        mLoginComplete = false;

        bool ok = await network.ConnectAsync(address, PlayerName);

        // Cancel is on screen throughout the await; if it was used, this result is for a socket
        // nobody wants any more.
        if (!ReferenceEquals(mConnecting, network))
        {
            network.Disconnect();
            return;
        }

        if (!ok)
        {
            mDisconnectedScreen.Reason = network.DisconnectReason ?? "Could not connect";
            CurrentState = GameState.Disconnected;
            mConnecting = null;
            return;
        }

        mConnectingScreen.CurrentPhase = ConnectingScreen.Phase.DownloadingTerrain;

        // Hand off rather than build here: everything past this point runs on a thread-pool thread,
        // and the session needs the main thread (see BuildNetworkSession).
        mLoginComplete = true;
    }

    // The multiplayer counterpart of InitWorld: an empty world fed by packets, and just enough
    // session state for the renderer. No terrain generation, no saved data, no structures.
    //
    // MAIN THREAD ONLY. PlayerArm and Hotbar create GL objects (VAOs, shaders, textures), and the
    // GL context belongs to the main thread - building them on JoinServer's continuation silently
    // produces dead handles.
    private void BuildNetworkSession(ClientNetwork network)
    {
        // Captured out of the factory because World owns the source but the packet handlers need it.
        // The factory form is required: the source needs the World that doesn't exist until the
        // constructor returns.
        NetworkChunkSource? source = null;
        mWorld = new World(seed: 0, settings: default, w => source = new NetworkChunkSource(w));
        mChunkSource = source;
        mWorld.Streamer.ChunkIntegrated = OnChunkIntegrated;

        mPlayer = new Player(Vector3.Zero, mWindow.Size.X / (float)mWindow.Size.Y);
        mPlayer.AssignNetworkId(network.LocalEntityId);

        mWorld.Players.Clear();
        mWorld.Players.Add(mPlayer);

        mPlayerArm = new PlayerArm(WorldTexture, ItemTexture, "Resources/world.png", "Resources/Items.png");
        mInventory = new PlayerInventory();
        mHotbar = new Hotbar(mBlockIconRenderer, ItemTexture, mInventory);
        mPlayer.SelectedBlock = mHotbar.GetSelectedBlock() ?? BlockType.Grass;

        // Constructed so the debug spawn key doesn't NRE; its Tick is skipped in multiplayer.
        mMobSpawner = new MobSpawner(mWorld, mGameRandom);

        // Audio. StartGame does this for singleplayer, and AudioManager's volumes default to 0,
        // so without it every sound in a multiplayer session plays silently.
        mAudioManager.SfxVol = mMainMenuScreen.SfxVolume;
        mAudioManager.MusicVol = mMainMenuScreen.MusicVolume;
        mAudioManager.PlayBackgroundMusic();

        // InitWorld's wiring - block and footstep sounds go through this callback.
        Entity.PlayStepSoundCallback = mAudioManager.PlayBlockContactSound;

        mChatScreen.Clear();
        mDeferredBlocks.Clear();
        mDeferredBlockCount = 0;
        ClearRemotePlayers();
        HookLocalBlockEdits();
        mPlayer.ServerOwnsHealth = true;
        mWorld.ServerDrivenBlockTicks = true;

        mRenderer.SetSession(mWorld, mPlayer, mTickSystem, mPlayerArm, mHotbar, mHudScreen);
    }

    // Draining on the MAIN thread is what lets handlers touch the world without locking.
    private void PumpNetwork()
    {
        if (mNetwork == null)
            return;

        // mNetwork is only non-null once the session is fully built, so no null-checking here.
        while (mNetwork.Inbox.TryDequeue(out var packet))
            HandleServerPacket(packet);

        // A dropped socket arrives as Connected going false, not as a packet, so it must be polled.
        if (!mNetwork.Connected && CurrentState is GameState.Playing or GameState.Connecting)
        {
            mDisconnectedScreen.Reason = mNetwork.DisconnectReason ?? "Connection lost";
            CurrentState = GameState.Disconnected;
            mNetwork = null;
        }
    }

    // Stage 5 handles almost nothing; later stages add cases here (chunks 6, entities 7, chat 8).
    private void HandleServerPacket(Packet packet)
    {
        var r = packet.OpenBody();

        switch (packet.Id)
        {
            case PacketId.KeepAlive:
                mNetwork!.Send(PacketId.KeepAlive, _ => { });   // beta echoes it straight back
                break;

            case PacketId.DisconnectKick:
                mDisconnectedScreen.Reason = r.ReadString();
                CurrentState = GameState.Disconnected;
                mNetwork = null;
                break;

            // Beta allocated a chunk before sending its data. We only care about the drop case;
            // the allocate is implied by the MapChunk that follows.
            case PacketId.PreChunk:
            {
                int chunkX = r.ReadInt();
                int chunkZ = r.ReadInt();
                if (!r.ReadBool())
                    mChunkSource?.OnChunkUnload(chunkX, chunkZ);
                break;
            }

            case PacketId.MapChunk:
            {
                int blockX = r.ReadInt();
                r.ReadShort();                                  // y - always 0, we send whole columns
                int blockZ = r.ReadInt();
                r.ReadByte(); r.ReadByte(); r.ReadByte();        // size-1 fields, fixed for us
                var blob = r.ReadBytes(r.ReadInt());

                // >> 4 not / 16: the shift floors toward negative infinity, which negative chunk
                // coordinates need.
                mChunkSource?.OnMapChunk(blockX >> 4, blockZ >> 4, blob);
                break;
            }

            // Sent once during login. Without it the player sits at (0,0,0), inside the ground.
            case PacketId.PlayerPositionLook:
            {
                float x = (float)r.ReadDouble();
                r.ReadDouble();                                 // stance (eye height)
                float y = (float)r.ReadDouble();
                float z = (float)r.ReadDouble();
                float yaw = r.ReadFloat();
                float pitch = r.ReadFloat();

                mPlayer.Position = new Vector3(x, y, z);
                mPlayer.Velocity = Vector3.Zero;                // or you respawn still falling
                mPlayer.Camera.SetRotation(pitch, yaw);
                break;
            }

            case PacketId.NamedEntitySpawn:   OnNamedEntitySpawn(r); break;
            case PacketId.EntityTeleport:     OnEntityTeleport(r); break;
            case PacketId.EntityRelativeMove: OnEntityRelativeMove(r, withLook: false); break;
            case PacketId.EntityLookRelMove:  OnEntityRelativeMove(r, withLook: true); break;
            case PacketId.EntityLook:         OnEntityLook(r); break;
            case PacketId.DestroyEntity:      OnDestroyEntity(r); break;
            case PacketId.Animation:          OnEntityAnimation(r); break;
            case PacketId.EntityEquipment:    OnEntityEquipment(r); break;
            case PacketId.ChatMessage:        OnChatMessage(r); break;
            case PacketId.OpenWindow:         OnOpenWindow(r); break;
            case PacketId.WindowItems:        OnWindowItems(r); break;
            case PacketId.SetSlot:            OnSetSlot(r); break;
            case PacketId.UpdateProgressBar:  OnProgressBar(r); break;
            case PacketId.BlockChange:        OnBlockChange(r); break;
            case PacketId.MultiBlockChange:   OnMultiBlockChange(r); break;
            case PacketId.TimeUpdate:         OnTimeUpdate(r); break;
            case PacketId.SoundEffect:        OnSoundEffect(r); break;
            case PacketId.UpdateHealth:       OnUpdateHealth(r); break;
            case PacketId.Respawn:            OnRespawn(r); break;
            case PacketId.EntityVelocity:     OnEntityVelocity(r); break;
            case PacketId.MobSpawn:           OnMobSpawn(r); break;
            case PacketId.AddObject:          OnAddObject(r); break;
            case PacketId.EntityPainting:     OnEntityPainting(r); break;
            case PacketId.PickupSpawn:        OnPickupSpawn(r); break;
            case PacketId.CollectItem:        OnCollectItem(r); break;

            // Safe to ignore only here: PacketLayout already consumed the packet's exact bytes off
            // the socket, so the stream is still aligned.
            default:
                break;
        }
    }

    // UpdateGameLogic doesn't run while Connecting, so nothing would tick the streamer and the
    // chunks the server is sending would sit in the source's queue forever.
    private void PumpArrivingChunks()
    {
        // Login finished on the connect thread; the session gets built here, where GL works.
        if (mLoginComplete && mConnecting is { } ready)
        {
            mLoginComplete = false;
            mConnecting = null;

            BuildNetworkSession(ready);
            mNetwork = ready;          // only now does PumpNetwork treat it as live
        }

        if (mWorld == null || mPlayer == null)
            return;

        mWorld.TickStreaming(mPlayer.Position);
        mWorld.Update(mPlayer.Position);

        // Start playing once the ground under our feet exists; the rest streams in around us.
        var here = ChunkMath.ToChunkCoord(mPlayer.Position);
        if (mWorld.Streamer.GetChunk(here.X, here.Z) != null)
        {
            CurrentState = GameState.Playing;
            SetCursorGrabbed(true);
        }
    }

    // TeardownWorld is null-safe on mWorld, so this works before Stage 6 makes one and after.
    private void LeaveServer()
    {
        // Both, because Cancel can land while a connect is still in flight.
        mConnecting?.Disconnect();
        mNetwork?.Disconnect();
        mConnecting = null;
        mNetwork = null;
        mLoginComplete = false;
        mChunkSource = null;

        TeardownWorld();
    }

    #endregion
}
