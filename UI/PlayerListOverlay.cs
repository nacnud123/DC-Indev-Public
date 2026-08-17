// Tab-held list of who's online. | Stage 8

using ImGuiNET;

namespace VoxelEngine.UI;

/// <summary>
/// Centred translucent panel, drawn only while Tab is held. The client knows the roster from
/// NamedEntitySpawn / DestroyEntity, so no extra packet is needed.
/// </summary>
public static class PlayerListOverlay
{
    private const float ROW_HEIGHT = 22f;
    private const float PANEL_WIDTH = 260f;

    public static void Render(string localName, IEnumerable<string> otherNames)
    {
        // Sorted with you first, so your own row doesn't move as people join and leave.
        var names = new List<string> { localName };
        names.AddRange(otherNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        var io = ImGui.GetIO();
        float height = ROW_HEIGHT * names.Count + 34f;

        ImGui.SetNextWindowPos(new Vector2((io.DisplaySize.X - PANEL_WIDTH) * 0.5f, 40f));
        ImGui.SetNextWindowSize(new Vector2(PANEL_WIDTH, height));
        ImGui.SetNextWindowBgAlpha(0.65f);

        ImGui.Begin("##playerlist", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                                    ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings);

        ImGui.TextDisabled($"Players ({names.Count})");
        ImGui.Separator();

        foreach (var name in names)
            ImGui.TextUnformatted(name);

        ImGui.End();
    }
}
