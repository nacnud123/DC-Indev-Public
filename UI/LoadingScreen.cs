// Loading screen, just has code to render "Loading..." on the screen. | DA | 2/5/26
using ImGuiNET;
using System;
using System.Numerics;

namespace VoxelEngine.UI;

/// <summary>
/// Fullscreen black "Loading..." overlay shown by <c>Game</c> while a world is being generated or read from disk. Purely presentational - has no update logic or state of its own.
/// </summary>
internal class LoadingScreen
{
    /// <summary>
    /// Draws a fullscreen, non-interactive black window with a centered "Loading..." label. Called once per frame while <c>GameState.Loading</c> is active.
    /// </summary>
    public void Render()
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Window covers the entire viewport so it fully obscures whatever was rendered before.
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(displaySize);

        // Strip every bit of normal window chrome/interaction - this is a static overlay, not a real window the player can move, resize, or focus.
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoTitleBar |
                                         ImGuiWindowFlags.NoResize |
                                         ImGuiWindowFlags.NoMove |
                                         ImGuiWindowFlags.NoCollapse |
                                         ImGuiWindowFlags.NoScrollbar |
                                         ImGuiWindowFlags.NoScrollWithMouse |
                                         ImGuiWindowFlags.NoBringToFrontOnFocus |
                                         ImGuiWindowFlags.NoFocusOnAppearing;

        // Opaque black background for the whole window.
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 1f));

        ImGui.Begin("LoadingGame", windowFlags);

        var windowSize = ImGui.GetWindowSize();
        var centerX = windowSize.X * 0.5f;
        var centerY = windowSize.Y * 0.5f;

        var titleText = "Loading...";
        ImGui.PushFont(ImGuiController.fontLarge);

        // Center the text by offsetting the cursor by half the measured text size in each axis.
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

