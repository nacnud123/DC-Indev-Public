// The server window. Stage 4 needs only enough to see output and type a command - the player list,
// TPS graph and stats panes are Stage 12. | Stage 4

using System.Text;
using ImGuiNET;

namespace VoxelEngine.Server;

/// <summary>
/// GUI THREAD ONLY. Everything it shows arrives through the log sink's concurrent queue; it must
/// never touch world state.
/// </summary>
public sealed class ServerGuiScreen
{
    private const int MAX_LOG_LINES = 2000;

    private readonly DuncanCraftServer mServer;
    private readonly QueuedLogSink mLog;
    private readonly ServerProperties mProps;

    private readonly List<(DateTime time, LogLevel level, string message)> mLines = new();
    private readonly byte[] mCommandBuffer = new byte[256];

    public ServerGuiScreen(DuncanCraftServer server, QueuedLogSink log, ServerProperties props)
    {
        mServer = server;
        mLog = log;
        mProps = props;
    }

    public void Render(int width, int height)
    {
        while (mLog.Pending.TryDequeue(out var entry))
            mLines.Add(entry);

        if (mLines.Count > MAX_LOG_LINES)
            mLines.RemoveRange(0, mLines.Count - MAX_LOG_LINES);

        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(width, height));
        ImGui.Begin("##server", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

        bool running = mServer.Running;
        ImGui.TextColored(running ? new Vector4(0.4f, 0.85f, 0.4f, 1f) : new Vector4(0.85f, 0.4f, 0.4f, 1f),
                          running ? "RUNNING" : "STOPPED");
        ImGui.SameLine();
        ImGui.TextDisabled($"| port {mProps.ServerPort}");

        ImGui.Separator();
        DrawLog();
        DrawCommandBar();

        ImGui.End();
    }

    private void DrawLog()
    {
        float reserved = ImGui.GetFrameHeightWithSpacing() + 8f;
        ImGui.BeginChild("##log", new Vector2(0f, -reserved), ImGuiChildFlags.None,
                         ImGuiWindowFlags.HorizontalScrollbar);

        foreach (var (time, level, message) in mLines)
        {
            ImGui.TextDisabled(time.ToString("HH:mm:ss"));
            ImGui.SameLine();
            ImGui.TextColored(ColourFor(level), message);
        }

        // Only stick to the bottom if already there, so scrolling up to read something doesn't get
        // yanked away by the next log line.
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1f)
            ImGui.SetScrollHereY(1f);

        ImGui.EndChild();
    }

    private static Vector4 ColourFor(LogLevel level) => level switch
    {
        LogLevel.Warning => new Vector4(0.95f, 0.80f, 0.35f, 1f),
        LogLevel.Error   => new Vector4(0.95f, 0.45f, 0.45f, 1f),
        LogLevel.Chat    => new Vector4(0.65f, 0.85f, 0.95f, 1f),
        LogLevel.Command => new Vector4(0.70f, 0.70f, 0.70f, 1f),
        _                => new Vector4(0.90f, 0.90f, 0.90f, 1f),
    };

    private void DrawCommandBar()
    {
        ImGui.SetNextItemWidth(-70f);
        bool submitted = ImGui.InputText("##cmd", mCommandBuffer, (uint)mCommandBuffer.Length,
                                         ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        bool clicked = ImGui.Button("Send", new Vector2(60f, 0f));

        if (!submitted && !clicked) return;

        string command = ReadBuffer(mCommandBuffer).Trim();
        Array.Clear(mCommandBuffer);
        if (submitted) ImGui.SetKeyboardFocusHere(-1);        // keep focus for the next command
        if (command.Length == 0) return;

        mLines.Add((DateTime.Now, LogLevel.Command, "> " + command));
        mServer.EnqueueConsoleCommand(command);               // the TICK thread runs it, not this one
    }

    /// Called from the window's Closing event. Blocks so the final save finishes before the process
    /// exits - a GUI server has no other shutdown path.
    public void StopServerAndWait()
    {
        if (!mServer.Running) return;

        mServer.EnqueueConsoleCommand("stop");
        // Generous: the final save now runs inline on the tick thread, and a big world's chunks take
        // longer than ten seconds to write on a slow disk.
        mServer.WaitForShutdown(TimeSpan.FromSeconds(60));
    }

    // ImGui hands back a NUL-terminated UTF-8 buffer; take everything before the first NUL.
    private static string ReadBuffer(byte[] buffer)
    {
        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, length);
    }
}
