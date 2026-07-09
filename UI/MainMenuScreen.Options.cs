using ImGuiNET;
using System.Numerics;
using VoxelEngine.Core;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    /// <summary>
    /// Renders the Options submenu (SFX/Music volume sliders, ASCII shader toggle, and navigation to Keybindings). Only called while <c>mCurrentState == MainMenuState.Options</c>; mirrors the manual panel/field layout pattern used by the other MainMenuScreen partials.
    /// </summary>
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

        // SFX Volume - label shows the live value; slider label is blank ("") since the value is already shown above via Text(), and "##sfxvolume" is an ID-only label that keeps ImGui's widget ID unique without rendering visible text.
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text($"SFX Volume  {mVolSfx}");
        y += 22f;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        ImGui.SliderInt("##sfxvolume", ref mVolSfx, 0, 100, "");

        // Music Volume - same label/slider pairing as SFX Volume above.
        y += 44f;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text($"Music Volume  {mVolMusic}");
        y += 22f;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        ImGui.SliderInt("##musicvolume", ref mVolMusic, 0, 100, "");
        y += 22f;

        // ASCII Shader toggle - pushes the setting straight through to Game.Instance so it takes effect immediately (post-processing shader swap), not just on menu close.
        y += 22f;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Checkbox("##ascii", ref mAsciiEnabled))
            Game.Instance.AsciiEnabled = mAsciiEnabled;
        ImGui.SameLine();
        ImGui.Text("ASCII Shader");

        ImGui.PopStyleColor();

        // Navigate to the Keybindings submenu (its own MainMenuState).
        y += 30f;
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Button("Keybindings", new Vector2(fieldW, BUTTON_HEIGHT)))
        {
            ClickSound();
            mCurrentState = MainMenuState.Keybindings;
        }
        PopBtn();

        // Back - returns to the Title screen state; volume/ascii changes above are applied live so there's nothing to persist/cancel here.
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
