using ImGuiNET;

namespace VoxelEngine.UI;

public sealed class DisconnectedScreen
{
    public string Reason = "Connection lost";
    public event Action? OnBackToMenu;

    private static readonly Vector4 ColRed = new(0.85f, 0.30f, 0.30f, 1f);
    private static readonly Vector4 ColTextDim = new(0.18f, 0.5f, 0.18f, 1f);

    public void Render()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(io.DisplaySize);

        ImGui.Begin("Disconnected", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);

        float cx = io.DisplaySize.X * 0.5f, cy = io.DisplaySize.Y * 0.5f;

        ImGui.PushFont(ImGuiController.fontLarge);
        const string title = "Disconnected";
        var ts = ImGui.CalcTextSize(title);
        ImGui.SetCursorPos(new Vector2(cx - ts.X * 0.5f, cy - 80f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColRed);
        ImGui.Text(title);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        // Strip any colour codes the server put in the reason before displaying it.
        string clean = StripColorCodes(Reason);
        var rs = ImGui.CalcTextSize(clean);
        ImGui.SetCursorPos(new Vector2(cx - rs.X * 0.5f, cy - 20f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        ImGui.TextWrapped(clean);
        ImGui.PopStyleColor();

        ImGui.SetCursorPos(new Vector2(cx - 110f, cy + 40f));
        if (ImGui.Button("Back to title screen", new Vector2(220f, 40f))) OnBackToMenu?.Invoke();

        ImGui.End();
    }

    private static string StripColorCodes(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "§.", "");

    public void OnClose() { }
}