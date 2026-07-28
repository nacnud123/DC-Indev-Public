using ImGuiNET;
using System.Numerics;
using VoxelEngine.Core;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    /// <summary>
    /// Renders the Options submenu (SFX/Music volume sliders, ASCII shader toggle, and
    /// navigation to Keybindings). Only called while <c>mCurrentState == MainMenuState.Options</c>;
    /// mirrors the manual panel/field layout pattern used by the other MainMenuScreen partials.
    /// </summary>
    private void RenderOptionsScreen(ImGuiWindowFlags flags)
    {
        ImGui.Begin("OptionsMenu", flags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;
        var cy = windowSize.Y * 0.5f;

        DrawTitle("Options", cx);

        float s = UIHelper.UI_SCALE;

        // Row spacing, scaled so it keeps pace with the font/widget growth ImGui applies
        // internally when UI_SCALE changes (see ImGuiController.ApplyUiScale) - fixed pixel
        // gaps here would otherwise get visually overrun by bigger text/widgets at higher scale.
        float labelToWidgetGap = 22f * s;  // a row's label down to its slider/input
        float sectionGap = 44f * s;        // end of one field's row down to the next label
        float navGap = 30f * s;            // last field down to the Keybindings button

        float formW = 440f * s;
        float fieldW = formW - PANEL_PAD * 2;

        // Total content height, derived from the same gap variables used to place the widgets
        // below - keeps the panel's background in sync with its content instead of drifting out
        // of sync with a hand-picked constant.
        float formH = PANEL_PAD * 2
            + labelToWidgetGap  // SFX label -> slider
            + sectionGap        // SFX slider -> Music label
            + labelToWidgetGap  // Music label -> slider
            + sectionGap        // Music slider -> Screen Scaling label
            + labelToWidgetGap  // Screen Scaling label -> input
            + sectionGap        // Screen Scaling input -> ASCII row
            + navGap            // ASCII row -> Keybindings button
            + BUTTON_HEIGHT;    // Keybindings button itself

        float formX = cx - formW * 0.5f;
        float formY = cy - formH * 0.5f - 20f * s;

        DrawPanel(formX, formY, formW, formH);

        float fieldX = formX + PANEL_PAD;
        float y = formY + PANEL_PAD;

        ImGui.PushStyleColor(ImGuiCol.Text, ColText);

        // SFX Volume - label shows the live value; slider label is blank ("") since the
        // value is already shown above via Text(), and "##sfxvolume" is an ID-only label
        // that keeps ImGui's widget ID unique without rendering visible text.
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text($"SFX Volume  {mVolSfx}");
        y += labelToWidgetGap;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        ImGui.SliderInt("##sfxvolume", ref mVolSfx, 0, 100, "");

        // Music Volume - same label/slider pairing as SFX Volume above.
        y += sectionGap;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text($"Music Volume  {mVolMusic}");
        y += labelToWidgetGap;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        ImGui.SliderInt("##musicvolume", ref mVolMusic, 0, 100, "");

        // UI Scale - same label/slider pairing as SFX Volume above.
        y += sectionGap;
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.Text("Screen Scaling");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        ImGui.Text($"({MIN_SCREEN_SCALING}-{MAX_SCREEN_SCALING})");
        ImGui.PopStyleColor(2);
        y += labelToWidgetGap;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        ImGui.SetNextItemWidth(fieldW);
        if (ImGui.InputInt("##screenscaling", ref mScreenScale))
        {
            ClickSound();
            mScreenScale = Math.Clamp(mScreenScale, MIN_SCREEN_SCALING, MAX_SCREEN_SCALING);
        }

        // ASCII Shader toggle - pushes the setting straight through to Game.Instance so it
        // takes effect immediately (post-processing shader swap), not just on menu close.
        y += sectionGap;
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Checkbox("##ascii", ref mAsciiEnabled))
            Game.Instance.AsciiEnabled = mAsciiEnabled;
        ImGui.SameLine();
        ImGui.Text("ASCII Shader");

        ImGui.PopStyleColor();

        // Navigate to the Keybindings submenu (its own MainMenuState).
        y += navGap;
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(fieldX, y));
        if (ImGui.Button("Keybindings", new Vector2(fieldW, BUTTON_HEIGHT)))
        {
            ClickSound();
            mCurrentState = MainMenuState.Keybindings;
        }
        PopBtn();

        // Back - returns to the Title screen state; volume/ascii changes above are applied
        // live so there's nothing to persist/cancel here.
        float by = formY + formH + 20f * s;
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, by));
        if (ImGui.Button("Back", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();

            Game.Instance.SetUiScale(mScreenScale);

            mCurrentState = MainMenuState.Title;
        }
        PopBtn();

        ImGui.End();
    }
}
