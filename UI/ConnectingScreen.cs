using ImGuiNET;

namespace VoxelEngine.UI;

public class ConnectingScreen
{
    public enum Phase { Connecting, LoggingIn, DownloadingTerrain }

    public Phase CurrentPhase = Phase.Connecting;
    public string ServerAddress = "";

    public event Action? OnCancel;
    
    private static readonly Vector4 ColText = new(0.25f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 ColTextDim = new(0.18f, 0.5f, 0.18f, 1f);

    public void Render()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(io.DisplaySize);
        
        ImGui.Begin("Connecting", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                  ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);

        float cx = io.DisplaySize.X * .5f, cy = io.DisplaySize.Y * .5f;

        string message = CurrentPhase switch
        {
            Phase.Connecting => "Connecting to the server...",
            Phase.LoggingIn => "Logging in...",
            _ => "Downloading terrain"
        };
        
        ImGui.PushFont(ImGuiController.fontLarge);
        var ms = ImGui.CalcTextSize(message);
        ImGui.SetCursorPos(new Vector2(cx - ms.X * .5f, cy - 40f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.Text(message);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        var addr = ImGui.CalcTextSize(ServerAddress);
        ImGui.SetCursorPos(new Vector2(cx - addr.X * .5f, cy + 4f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.Text(ServerAddress);
        ImGui.PopStyleColor();
        
        ImGui.SetCursorPos(new Vector2(cx - 110f, cy + 60f));
        if(ImGui.Button("Cancel", new Vector2(220f, 40f)))
            OnCancel?.Invoke();
        
        ImGui.End();
    }

    public void OnClose()
    {
        
    }
}