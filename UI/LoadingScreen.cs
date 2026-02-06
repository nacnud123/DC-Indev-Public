// Loading screen, just has code to render "Loading..." on the screen. | DA | 2/5/26
using ImGuiNET;
using System;
using System.Numerics;

namespace VoxelEngine.UI;

internal class LoadingScreen
{
    public void Render()
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(displaySize);

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoTitleBar |
                                         ImGuiWindowFlags.NoResize |
                                         ImGuiWindowFlags.NoMove |
                                         ImGuiWindowFlags.NoCollapse |
                                         ImGuiWindowFlags.NoScrollbar |
                                         ImGuiWindowFlags.NoScrollWithMouse |
                                         ImGuiWindowFlags.NoBringToFrontOnFocus |
                                         ImGuiWindowFlags.NoFocusOnAppearing;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 1f));

        ImGui.Begin("LoadingGame", windowFlags);

        var windowSize = ImGui.GetWindowSize();
        var centerX = windowSize.X * 0.5f;
        var centerY = windowSize.Y * 0.5f;

        var titleText = "Loading...";
        ImGui.PushFont(ImGuiController.fontLarge);

        var titleSize = ImGui.CalcTextSize(titleText);
        ImGui.SetCursorPos(new Vector2(centerX - titleSize.X * 0.5f, centerY - titleSize.Y * 0.5f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text(titleText);
        ImGui.PopStyleColor();

        ImGui.PopFont();
        ImGui.End();

        ImGui.PopStyleColor();
    }
}

