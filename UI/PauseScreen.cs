// Main pause menu screen class, holds stuff related to rendering the pause menu. | DA | 2/5/26
using ImGuiNET;
using System;
using System.Numerics;
using VoxelEngine.Core;

namespace VoxelEngine.UI;
/// <summary>
/// Semi-transparent pause overlay shown over the frozen game world (<c>GameState.Paused</c>). Presents Resume/Save/Quit buttons; Game.cs subscribes to the events below to actually perform the state transitions, save, or application exit.
/// </summary>
public class PauseScreen
{
    /// <summary>Raised when "Quit" is clicked; Game.cs handles exiting to the main menu / app.</summary>
    public event Action OnPauseQuitGame;
    /// <summary>Raised when "Resume Game" is clicked; Game.cs unpauses and hides this screen.</summary>
    public event Action OnResumeGame;
    /// <summary>Raised when "Save Game" is clicked; Game.cs writes the world/player state to disk.</summary>
    public event Action OnSaveGame;

    /// <summary>Draws the pause overlay and the three action buttons. Called once per frame while paused.</summary>
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

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.5f));

        ImGui.Begin("PauseGame", windowFlags);

        var windowSize = ImGui.GetWindowSize();
        var contentWidth = 400f;

        var centerX = windowSize.X * 0.5f;
        var centerY = windowSize.Y * 0.5f;

        // Title
        var titleText = "Pause";
        ImGui.PushFont(ImGuiController.fontLarge);

        var titleSize = ImGui.CalcTextSize(titleText);
        ImGui.SetCursorPos(new Vector2(centerX - titleSize.X * 0.5f, centerY - 120f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text(titleText);
        ImGui.PopStyleColor();

        ImGui.PopFont();

        // Buttons — 3 stacked vertically, each buttonHeight + buttonSpacing apart from the last
        var buttonWidth = 180f;
        var buttonHeight = 40f;
        var buttonSpacing = 12f;
        var buttonX = centerX - buttonWidth * 0.5f;
        var buttonY = centerY - 10f;

        // Resume button
        ImGui.SetCursorPos(new Vector2(buttonX, buttonY));
        if (ImGui.Button("Resume Game", new Vector2(buttonWidth, buttonHeight)))
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/UI/Click1.ogg", Game.Instance.AudioManager.SfxVol, false);
            OnResumeGame?.Invoke();
        }

        // Save Game button
        ImGui.SetCursorPos(new Vector2(buttonX, buttonY + buttonHeight + buttonSpacing));
        if (ImGui.Button("Save Game", new Vector2(buttonWidth, buttonHeight)))
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/UI/Click1.ogg", Game.Instance.AudioManager.SfxVol, false);
            OnSaveGame?.Invoke();
        }

        // Quit button - styled red/destructive via pushed Button/Hovered/Active colors, popped together below (PopStyleColor(3)) once the button is drawn.
        ImGui.SetCursorPos(new Vector2(buttonX, buttonY + (buttonHeight + buttonSpacing) * 2f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.1f, 0.1f, 1.0f));

        if (ImGui.Button("Quit", new Vector2(buttonWidth, buttonHeight)))
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/UI/Click1.ogg", Game.Instance.AudioManager.SfxVol, false);
            OnPauseQuitGame?.Invoke();
        }

        ImGui.PopStyleColor(3);

        ImGui.End();

        ImGui.PopStyleColor();
    }
}
