// Death screen, shown when the player's health reaches zero. | DA | 3/3/26

using ImGuiNET;
using System;
using System.Numerics;
using VoxelEngine.Core;

namespace VoxelEngine.UI;

/// <summary>
/// Fullscreen "You Died!" overlay shown when the player's health hits zero (<c>GameState.Died</c>). Offers a single "Main Menu" button to bail out of the run; <c>Game.cs</c> subscribes to <see cref="OnReturnToMainMenu"/> to drive the actual state transition and any respawn/save cleanup.
/// </summary>
public class DeathScreen
{
    /// <summary>Raised when the player clicks "Main Menu"; Game.cs handles the actual state change.</summary>
    public event Action OnReturnToMainMenu;

    /// <summary>Draws the fullscreen death overlay and handles the Main Menu button click. Called once per frame while dead.</summary>
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

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.75f));

        ImGui.Begin("DeathScreen", windowFlags);

        var windowSize = ImGui.GetWindowSize();
        var centerX = windowSize.X * 0.5f;
        var centerY = windowSize.Y * 0.5f;

        // Title (positioned above screen center; button block below fills the rest of the layout)
        var titleText = "You Died!";
        ImGui.PushFont(ImGuiController.fontLarge);

        var titleSize = ImGui.CalcTextSize(titleText);
        ImGui.SetCursorPos(new Vector2(centerX - titleSize.X * 0.5f, centerY - 80f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.1f, 0.1f, 1.0f));
        ImGui.Text(titleText);
        ImGui.PopStyleColor();

        ImGui.PopFont();

        // Main Menu button - plays a click SFX and fires the event; Game.cs does the actual state transition (e.g. tearing down the world, showing MainMenuScreen).
        var buttonWidth = 180f;
        var buttonHeight = 44f;
        ImGui.SetCursorPos(new Vector2(centerX - buttonWidth * 0.5f, centerY + 20f));

        if (ImGui.Button("Main Menu", new Vector2(buttonWidth, buttonHeight)))
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/UI/Click1.ogg", Game.Instance.AudioManager.SfxVol,
                false);
            OnReturnToMainMenu?.Invoke();
        }

        ImGui.End();

        ImGui.PopStyleColor();
    }
}