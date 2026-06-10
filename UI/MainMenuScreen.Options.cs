using ImGuiNET;
using System.Numerics;
using VoxelEngine.Core;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    private void RenderOptionsScreen(ImGuiWindowFlags flags)
    {
        ImGui.Begin("OptionsMenu", flags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;
        var cy = windowSize.Y * 0.5f;

        DrawTitle("Options", cx);

        float formW = 440f;
        float formH = 270f;
        float formX = cx - formW * 0.5f;
        float formY = cy - formH * 0.5f - 20f;

        DrawPanel(formX, formY, formW, formH);

        float fieldW = formW - PANEL_PAD * 2;
        float fieldX = formX + PANEL_PAD;
        float y = formY + PANEL_PAD;

        ImGui.PushStyleColor(ImGuiCol.Text, ColText);

        // SFX Volume
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text($"SFX Volume  {mVolSfx}");
        y += 22f;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        ImGui.SliderInt("##sfxvolume", ref mVolSfx, 0, 100, "");

        // Music Volume
        y += 44f;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text($"Music Volume  {mVolMusic}");
        y += 22f;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        ImGui.SliderInt("##musicvolume", ref mVolMusic, 0, 100, "");
        y += 22f;

        // ASCII Shader
        y += 22f;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Checkbox("##ascii", ref mAsciiEnabled))
            Game.Instance.AsciiEnabled = mAsciiEnabled;
        ImGui.SameLine();
        ImGui.Text("ASCII Shader");

        ImGui.PopStyleColor();

        y += 30f;
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Button("Keybindings", new Vector2(fieldW, BUTTON_HEIGHT)))
        {
            ClickSound();
            mCurrentState = MainMenuState.Keybindings;
        }
        PopBtn();

        // Back
        float by = formY + formH + 20f;
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, by));
        if (ImGui.Button("Back", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();
            mCurrentState = MainMenuState.Title;
        }
        PopBtn();

        ImGui.End();
    }
}
