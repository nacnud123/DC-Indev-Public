using ImGuiNET;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    private const string RECENT_SERVERS_FILE = "servers.txt";
    private const string USERNAME_FILE = "username.txt";
    private const int MAX_RECENT_SERVERS = 8;

    private void RenderMultiplayerScreen(ImGuiWindowFlags windowFlags)
    {
        ImGui.Begin("MultiplayerMenu", windowFlags);

        var windowsSize = ImGui.GetWindowSize();
        float cx = windowsSize.X * .5f;
        float cy = windowsSize.Y * .5f;

        ImGui.PushFont(ImGuiController.fontLarge);
        var title = "Join Server";
        var ts = ImGui.CalcTextSize(title);

        ImGui.SetCursorPos(new Vector2(cx - ts.X * .5f, cy - 200f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.Text(title);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, cy - 175f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        ImGui.Text("Username");
        ImGui.PopStyleColor();

        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, cy - 153f));
        ImGui.SetNextItemWidth(BUTTON_WIDTH);
        ImGui.InputText("##username", mUsernameBuffer, (uint)mUsernameBuffer.Length);

        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, cy - 118f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        ImGui.Text("Server Address");
        ImGui.PopStyleColor();

        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, cy - 96f));
        ImGui.SetNextItemWidth(BUTTON_WIDTH);

        bool submitted = ImGui.InputText("##serveraddr", mServerAddressBuffer, (uint)mServerAddressBuffer.Length,
            ImGuiInputTextFlags.EnterReturnsTrue);

        string address = GetStringFromBuffer(mServerAddressBuffer).Trim();
        bool canConnect = address.Length > 0;

        PushDisableableBtn(canConnect, red: false);
        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * .5f, cy - 55f));
        bool clicked = ImGui.Button("Connect", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT));
        PopDisableableBtn(canConnect, false);

        if (canConnect && (clicked || submitted))
        {
            ClickSound();
            RememberServer(address);
            SaveUsername();
            OnJoinServer?.Invoke(address);
        }
        
        // Recent servers list
        if (mRecentServers.Count > 0)
        {
            ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, cy - 12f));
            ImGui.PushStyleColor(ImGuiCol.Text, ColTextMuted);
            ImGui.Text("Recent");
            ImGui.PopStyleColor();

            for (int i = 0; i < mRecentServers.Count; i++)
            {
                ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f,
                    cy + 10f + i * (BUTTON_HEIGHT * 0.7f + 4f)));
                PushGreenBtn();
                if (ImGui.Button(mRecentServers[i] + $"##recent{i}",
                        new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT * 0.7f)))
                {
                    ClickSound();
                    SetInputBuffer(mServerAddressBuffer, mRecentServers[i]);
                }
                PopBtn();
            }
        }

        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, windowsSize.Y - 90f));
        if (ImGui.Button("Back", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();
            mCurrentState = MainMenuState.Title;
        }

        PopBtn();

        ImGui.End();
    }

    // Called alongside LoadRecentServers when the screen opens.
    private void LoadUsername()
    {
        if (GetStringFromBuffer(mUsernameBuffer).Length > 0)
            return;                                   // already typed something this session

        string saved = File.Exists(USERNAME_FILE) ? File.ReadAllText(USERNAME_FILE).Trim() : "";
        SetInputBuffer(mUsernameBuffer, saved.Length > 0 ? saved : "Player");
    }

    private void SaveUsername() => File.WriteAllText(USERNAME_FILE, PlayerName);

    private void LoadRecentServers()
    {
        mRecentServers = File.Exists(RECENT_SERVERS_FILE)
            ? File.ReadAllLines(RECENT_SERVERS_FILE)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Take(MAX_RECENT_SERVERS).ToList()
            : new List<string>();
    }

    private void RememberServer(string address)
    {
        mRecentServers.Remove(address);
        mRecentServers.Insert(0, address);
        if (mRecentServers.Count > MAX_RECENT_SERVERS)
        {
            mRecentServers.RemoveRange(MAX_RECENT_SERVERS, mRecentServers.Count - MAX_RECENT_SERVERS);
        }

        File.WriteAllLines(RECENT_SERVERS_FILE, mRecentServers);
    }
}