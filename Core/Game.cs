// Main game file, does things like generate world and init stuff and move between states | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
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
    public Texture WorldTexture { get; private set; } = null!;
    public ParticleSystem ParticleSystem { get; private set; } = null!;

    // UI
    private ImGuiController mImGuiController = null!;
    private PauseScreen mPauseScreen = null!;
    private LoadingScreen mLoadingScreen = null!;
    private MainMenuScreen mMainMenuScreen = null!;
    private InventoryScreen mInventoryScreen = null!;

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

    Vector3 mSpawnPos;

    // Game State
    public GameState CurrentState { get; private set; }
    public Player GetPlayer { get => mPlayer; }

    public Game(int width, int height, string title)
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new Vector2i(width, height),
            Title = title,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
        })
    { }

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

    protected override void OnUnload()
    {
        base.OnUnload();

        mWorld?.Dispose();
        mShader?.Dispose();
        mCrosshair?.Dispose();
        mBlockHighlight?.Dispose();
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

    private void LoadResources()
    {
        mShader = new Shader( File.ReadAllText("Shaders/vertex.glsl"), File.ReadAllText("Shaders/fragment.glsl"));
        WorldTexture = Texture.LoadFromFile("Resources/world.png");

        mCrosshair = new Crosshair();
        mBlockHighlight = new BlockHighlight();

        mTickSystem = new TickSystem();
        ParticleSystem = new ParticleSystem();
    }

    private void InitWorld()
    {
        mWorld = new World(mNewWorldSize);
        mWorld.BuildAllMeshes();

        int spawnX = mWorld.SizeInChunks * Chunk.WIDTH / 2;
        int spawnZ = mWorld.SizeInChunks * Chunk.DEPTH / 2;
        mSpawnPos = mWorld.FindSpawnPosition(spawnX, spawnZ);
        mPlayer = new Player(mSpawnPos, Size.X / (float)Size.Y);

        MakeSpawnHouse();
        SpawnSomePigs();

    }

    private void SpawnSomePigs()
    {
        var random = new Random();

        for (int i = 0; i < INITIAL_PIG_COUNT; i++)
        {
            int offsetX = random.Next(-PIG_SPAWN_RADIUS, PIG_SPAWN_RADIUS);
            int offsetZ = random.Next(-PIG_SPAWN_RADIUS, PIG_SPAWN_RADIUS);

            int x = (int)mSpawnPos.X + offsetX;
            int z = (int)mSpawnPos.Z + offsetZ;

            Vector3 pigSpawn = mWorld.FindSpawnPosition(x, z);
            mWorld.AddEntity(new Pig(pigSpawn));
        }
    }


    private void MakeSpawnHouse()
    {
        int sx = (int)MathF.Floor(mSpawnPos.X);
        int sy = (int)MathF.Floor(mSpawnPos.Y);
        int sz = (int)MathF.Floor(mSpawnPos.Z);

        for (int x = -4; x <= 4; x++)
        {
            for (int z = -4; z <= 4; z++)
            {
                for (int y = 0; y <= 5; y++)
                {
                    mWorld.SetBlock(sx + x, sy + y, sz + z, BlockType.Air);
                }
            }
        }

        for (int x = -3; x <= 3; x++)
        {
            for (int z = -3; z <= 3; z++)
            {
                mWorld.SetBlock(sx + x, sy - 1, sz + z, BlockType.Stone);
                mWorld.SetBlock(sx + x, sy + 4, sz + z, BlockType.Planks);

                bool isWall = x == -3 || x == 3 || z == -3 || z == 3;
                bool isDoor = x == 0 && z == -3;

                for (int y = 0; y < 4; y++)
                {
                    if (isWall && !(isDoor && y <= 1))
                        mWorld.SetBlock(sx + x, sy + y, sz + z, BlockType.Planks);
                }
            }
        }
    }

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

        float dt = (float)args.Time;
        mImGuiController.Update(this, dt);

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
        
        // -----------------------------------

        // Render Keys
        if (KeyboardState.IsKeyPressed(Keys.Equal) || KeyboardState.IsKeyPressed(Keys.KeyPadAdd))
            mPlayer.Camera.RenderDistance = MathF.Min(mPlayer.Camera.RenderDistance + 32f, 512f);

        if (KeyboardState.IsKeyPressed(Keys.Minus) || KeyboardState.IsKeyPressed(Keys.KeyPadSubtract))
            mPlayer.Camera.RenderDistance = MathF.Max(mPlayer.Camera.RenderDistance - 32f, 32f);
        // -----------------------------------
        
        
        // Mouse
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
        // -----------------------------------

        // Player
        mPlayer.Update(mWorld, KeyboardState, dt);
        // -----------------------------------

        // Block/entity interaction
        if (MouseState.IsButtonPressed(MouseButton.Left))
            LeftClick();

        if (MouseState.IsButtonPressed(MouseButton.Right))
            mPlayer.HandleBlockInteraction(mWorld, false, true);
        // -----------------------------------
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        mImGuiController.MouseScroll(e.Offset);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        mImGuiController.PressChar((char)e.Unicode);
    }

    private void UpdateGameLogic(float dt)
    {
        int ticks = mTickSystem.Accumulate(dt);
        for (int i = 0; i < ticks; i++)
        {
            mTickCount++;
            mWorld.TickEntities();
            mWorld.Update();
            mWorld.RandomDisplayUpdates(mPlayer.Position);
        }

        ParticleSystem.Update(dt, mWorld);
        ParticleSystem.UpdateSmoke(dt, mWorld);
    }

    private void LeftClick()
    {
        var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);

        switch (hit.Type)
        {
            case RaycastHitType.Entity:
                hit.Entity!.TakeDamage(10);
                break;
            case RaycastHitType.Block:
                mPlayer.HandleBlockInteraction(mWorld, true, false);
                break;
        }
    }

    #endregion

    #region Render

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

    private void RenderWorld(Matrix4 view, Matrix4 proj)
    {
        mShader.Use();
        mShader.SetMatrix4("model", Matrix4.Identity);
        mShader.SetMatrix4("view", view);
        mShader.SetMatrix4("projection", proj);

        // Lighting
        mShader.SetVector3("lightDir", new Vector3(-0.5f, -1f, -0.3f).Normalized());
        mShader.SetVector3("lightColor", new Vector3(1f, 1f, 0.95f));
        mShader.SetFloat("ambientStrength", 0.3f);

        // Fog
        float fogDist = mPlayer.Camera.RenderDistance;
        mShader.SetVector3("fogColor", new Vector3(0.5f, 0.7f, 1.0f));
        mShader.SetFloat("fogStart", fogDist * 0.4f);
        mShader.SetFloat("fogEnd", fogDist * 0.9f);

        // Texture
        WorldTexture.Use(TextureUnit.Texture0);
        mShader.SetInt("blockTexture", 0);

        mWorld.Render(mPlayer.Camera);
    }

    private void RenderEntities(Matrix4 view, Matrix4 proj)
    {
        mWorld.RenderEntities(view, proj, mPlayer.Camera.Position, mPlayer.Camera.RenderDistance);
    }

    private void RenderParticles(Matrix4 view, Matrix4 proj)
    {
        GL.DepthMask(false);
        ParticleSystem.Render(view, proj, WorldTexture);
        ParticleSystem.RenderSmoke(view, proj);
        GL.DepthMask(true);
    }

    private void RenderBlockHighlight(Matrix4 view, Matrix4 proj)
    {
        var hit = mWorld.Raycast(mPlayer.Camera.Position, mPlayer.Camera.Front);

        if (hit.Type == RaycastHitType.Block)
        {
            var boundsMin = Terrain.Blocks.BlockRegistry.GetBoundsMin(hit.BlockType);
            var boundsMax = Terrain.Blocks.BlockRegistry.GetBoundsMax(hit.BlockType);
            mBlockHighlight.Render(hit.BlockPos, view, proj, boundsMin, boundsMax);
        }
    }

    private void RenderHud()
    {
        GL.Disable(EnableCap.DepthTest);
        mCrosshair.Render();
        GL.Enable(EnableCap.DepthTest);
    }

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

            Title = $"DuncanCraft 2000 InDev | FPS:{mFrameCount} TPS:{mTickCount} | {mPlayer.SelectedBlock} | {pos.X:F1},{pos.Y:F1},{pos.Z:F1} Chunk:{chunkX},{chunkZ} [{mode}] Render:{renderDist}";

            mFrameCount = 0;
            mTickCount = 0;
            mFpsTimer = 0;
        }
    }

    #endregion

    #region Game State

    private void StartGame(int worldSize)
    {
        mNewWorldSize = worldSize;
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
    }

    #endregion
}
