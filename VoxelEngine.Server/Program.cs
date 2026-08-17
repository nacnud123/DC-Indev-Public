// Dedicated-server entry point. A window, always - there is no headless mode. | Stage 4

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using VoxelEngine.Rendering;
using VoxelEngine.UI;

namespace VoxelEngine.Server;

public static class Program
{
    private static IWindow mWindow = null!;
    private static IInputContext mInput = null!;
    private static ImGuiController mImGui = null!;
    private static ServerGuiScreen mGui = null!;

    public static void Main(string[] args)
    {
        var props = ServerProperties.LoadOrCreate("server.properties");
        var log = new QueuedLogSink();
        var server = new DuncanCraftServer(props, log);

        mGui = new ServerGuiScreen(server, log, props);

        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(900, 620);
        options.Title = $"DuncanCraft Server - port {props.ServerPort}";
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default,
                                      new APIVersion(3, 3));
        options.VSync = true;              // a log window has no reason to render at 1000 fps

        mWindow = Window.Create(options);
        mWindow.Load += OnLoad;
        mWindow.Update += dt => mImGui.Update((float)dt, mInput.Mice[0], mInput.Keyboards[0]);
        mWindow.Render += OnRender;
        mWindow.Resize += size => mImGui.WindowResized(size.X, size.Y);
        mWindow.Closing += OnClosing;

        // The server ticks on its own thread from here; the window loop below only draws it.
        server.Start();

        mWindow.Run();
    }

    private static void OnLoad()
    {
        GlContext.Gl = GL.GetApi(mWindow);
        mInput = mWindow.CreateInput();
        mImGui = new ImGuiController(mWindow.Size.X, mWindow.Size.Y);

        // Key events alone are not text: ImGui takes typed characters separately, and without this
        // the command bar accepts focus and then ignores everything typed into it.
        foreach (var keyboard in mInput.Keyboards)
            keyboard.KeyChar += (_, c) => mImGui.PressChar(c);
    }

    private static void OnRender(double _)
    {
        var gl = GlContext.Gl;
        gl.ClearColor(0.09f, 0.09f, 0.11f, 1f);
        gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        mGui.Render(mWindow.Size.X, mWindow.Size.Y);
        mImGui.Render();
    }

    private static void OnClosing()
    {
        // Closing the window is the only way out, so it has to be a clean shutdown - otherwise
        // every session ends by discarding whatever the last 2 minutes of play changed.
        mGui.StopServerAndWait();
        mImGui.Dispose();
    }
}
