using ImGuiNET;
using System;
using System.Numerics;
using VoxelEngine.Saving;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    private void RenderNewGameScreen(ImGuiWindowFlags flags)
    {
        ImGui.Begin("NewGameMenu", flags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;
        var cy = windowSize.Y * 0.5f;

        DrawTitle("Create New World", cx);

        const float labelH = 22f;
        const float inputH = 22f;
        const float rowGap = 10f;
        const float bigGap = 50f;

        float formW = 440f;
        float formH = PANEL_PAD
                      + labelH + inputH + rowGap    // Name
                      + BUTTON_HEIGHT + rowGap       // Type
                      + labelH + inputH + rowGap     // Size
                      + BUTTON_HEIGHT + rowGap       // Theme
                      + BUTTON_HEIGHT + bigGap       // Creative + large gap
                      + BUTTON_HEIGHT + rowGap       // Create
                      + BUTTON_HEIGHT               // Cancel
                      + PANEL_PAD;

        float formX = cx - formW * 0.5f;
        float formY = cy - formH * 0.5f - 20f;

        DrawPanel(formX, formY, formW, formH);

        float fieldW = formW - PANEL_PAD * 2;
        float fieldX = formX + PANEL_PAD;
        float y = formY + PANEL_PAD;

        // World Name
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text("World Name");
        ImGui.PopStyleColor();
        y += labelH;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        ImGui.InputText("##worldname", mWorldNameBuffer, (uint)mWorldNameBuffer.Length);
        y += inputH + rowGap;

        // Type
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Button($"Type: {WorldTypes[mWorldType]} >##type", new Vector2(fieldW, BUTTON_HEIGHT)))
        {
            ClickSound();
            mWorldType = (mWorldType + 1) % WorldTypes.Length;
        }
        PopBtn();
        y += BUTTON_HEIGHT + rowGap;

        // World Size
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text("World Size");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        ImGui.Text("(even, 8-4096)");
        ImGui.PopStyleColor(2);
        y += labelH;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        var previous = mWorldSize;
        if (ImGui.InputInt("##worldsize", ref mWorldSize))
        {
            ClickSound();
            mWorldSize = Math.Clamp(mWorldSize, MIN_WORLD_SIZE, MAX_WORLD_SIZE);
            if ((mWorldSize & 1) != 0)
            {
                mWorldSize += mWorldSize > previous ? 1 : -1;
                mWorldSize = Math.Clamp(mWorldSize, MIN_WORLD_SIZE, MAX_WORLD_SIZE);
            }
        }
        y += inputH + rowGap;

        // Theme
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Button($"Theme: {WorldThemes[mWorldTheme]} >##theme", new Vector2(fieldW, BUTTON_HEIGHT)))
        {
            ClickSound();
            mWorldTheme = (mWorldTheme + 1) % WorldThemes.Length;
        }
        PopBtn();
        y += BUTTON_HEIGHT + rowGap;

        // Creative Mode
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, ColText);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ColFrame);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ColFrameHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ColFrameActive);

        if (ImGui.Checkbox("##creative", ref mIsCreative))
            ClickSound();

        ImGui.SameLine();
        ImGui.Text("Creative Mode");
        ImGui.PopStyleColor(5);
        y += BUTTON_HEIGHT + bigGap;

        // Create
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Button("Create", new Vector2(fieldW, BUTTON_HEIGHT)))
        {
            ClickSound();
            string worldName = GetStringFromBuffer(mWorldNameBuffer).Trim();
            if (string.IsNullOrEmpty(worldName))
                worldName = "New World";
            Serialization.WorldName = worldName;
            Serialization.CreateWorld(worldName, null, mWorldSize, mWorldType, mWorldTheme, isCreative: mIsCreative);
            OnStartGame?.Invoke(mWorldSize, mVolSfx, mVolMusic, mWorldType, mWorldTheme, mIsCreative);
        }
        PopBtn();
        y += BUTTON_HEIGHT + rowGap;

        // Cancel
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Button("Cancel", new Vector2(fieldW, BUTTON_HEIGHT)))
        {
            ClickSound();
            mWorldsLoaded = false;
            mCurrentState = MainMenuState.WorldSelection;
        }
        PopBtn();

        ImGui.End();
    }
}
