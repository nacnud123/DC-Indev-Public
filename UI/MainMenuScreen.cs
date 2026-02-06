// Main main menu class, holds stuff related to rendering the main menu. | DA | 2/5/26
using ImGuiNET;
using System;
using System.Numerics;

namespace VoxelEngine.UI;
public class MainMenuScreen
{
    private const int MIN_WORLD_SIZE = 8;
    private const int MAX_WORLD_SIZE = 4096;

    private int mWorldSize = 8;

    public event Action OnTitleQuitGame;
    public event Action<int> OnStartGame;

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

        ImGui.Begin("MainMenu", windowFlags);

        var windowSize = ImGui.GetWindowSize();
        var contentWidth = 400f;

        var centerX = windowSize.X * 0.5f;
        var centerY = windowSize.Y * 0.5f;

        // Title
        var titleText = "DC Indev";
        ImGui.PushFont(ImGuiController.fontLarge);

        var titleSize = ImGui.CalcTextSize(titleText);
        ImGui.SetCursorPos(new Vector2(centerX - titleSize.X * 0.5f, centerY - 120f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text(titleText);
        ImGui.PopStyleColor();

        ImGui.PopFont();

        // Version text
        ImGui.PopStyleColor();

        ImGui.SetCursorPos(new Vector2(10, 10));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text("Version 1.0A - RAM usage may be a little high");


        // Controls Text\
        var controlsText = @"
            W A S D = Move
            LMB = Break block / Kill entity
            RMB = Place block
            0-9 = Choose blocks
            R = Reset position
            X = Toggle wireframe
            P = Spawn pig
            Esc = Pause
            Tab = Toggle cursor lock
            F = Toggle fly mode
            E = Toggle inventory
            Space = Jump / Fly up
            Ctrl = Fly down
            Shift = Sprint
            + / - = Increase / decrease render distance
            Mouse = Look
            ";
        ImGui.SetWindowFontScale(1.0f); // 2X size

        var controlsSize = ImGui.CalcTextSize(controlsText);
        ImGui.SetCursorPos(new Vector2(-70f, windowSize.Y - controlsSize.Y - 10));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text(controlsText);
        ImGui.PopStyleColor();

        ImGui.SetWindowFontScale(1.0f); // Set back to default

        // World Size
        ImGui.SetCursorPos(new Vector2(centerX - 200f, centerY - 80f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text("World Size:");

        ImGui.SetCursorPos(new Vector2(centerX - 200f, centerY - 60f));
        ImGui.SetNextItemWidth(400f);

        var previous = mWorldSize;
        if (ImGui.InputInt("##worldsize", ref mWorldSize))
        {
            mWorldSize = Math.Clamp(mWorldSize, MIN_WORLD_SIZE, MAX_WORLD_SIZE);

            if ((mWorldSize & 1) != 0)
            {
                if (mWorldSize > previous)
                {
                    mWorldSize += 1;
                }
                else
                {
                    mWorldSize -= 1;
                }

                mWorldSize = Math.Clamp(mWorldSize, MIN_WORLD_SIZE, MAX_WORLD_SIZE);
            }
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(snaps to even)");
        ImGui.PopStyleColor();

        // Buttons
        var buttonWidth = 150f;
        var buttonHeight = 40f;
        var buttonSpacing = 20f;
        var totalButtonWidth = (buttonWidth * 2) + buttonSpacing;
        var buttonStartX = centerX - totalButtonWidth * 0.5f;
        var buttonY = centerY + 30f;

        // Resume button
        ImGui.SetCursorPos(new Vector2(buttonStartX, buttonY));

        if (ImGui.Button("Start Game", new Vector2(buttonWidth, buttonHeight)))
        {
            OnStartGame?.Invoke(mWorldSize);
        }

        // Quit button
        ImGui.SetCursorPos(new Vector2(buttonStartX + buttonWidth + buttonSpacing, buttonY));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.1f, 0.1f, 1.0f));

        if (ImGui.Button("Quit", new Vector2(buttonWidth, buttonHeight)))
        {
            OnTitleQuitGame?.Invoke();
        }

        ImGui.PopStyleColor(3);

        ImGui.End();

        ImGui.PopStyleColor();
    }
}
