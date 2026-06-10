using ImGuiNET;
using System.Numerics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VoxelEngine.Core;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    private void RenderTitleScreen(ImGuiWindowFlags windowFlags)
    {
        ImGui.Begin("TitleScreenMainMenu", windowFlags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;
        var cy = windowSize.Y * 0.5f;

        // Version (top-left)
        ImGui.SetCursorPos(new Vector2(12, 8));
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        ImGui.Text("Version 1.1R\nBy: Duncan Armstrong");
        ImGui.PopStyleColor();

        // Title
        ImGui.PushFont(ImGuiController.fontLarge);
        var titleText = "DC Indev";
        var ts = ImGui.CalcTextSize(titleText);
        ImGui.SetCursorPos(new Vector2(cx - ts.X * 0.5f, cy - 160f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.Text(titleText);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        // Splash text (dimmer, centered)
        var ss = ImGui.CalcTextSize(mCurrentSplash);
        ImGui.SetCursorPos(new Vector2(cx - ss.X * 0.5f, cy - 115f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        ImGui.Text(mCurrentSplash);
        ImGui.PopStyleColor();

        // Buttons
        float bx = cx - BUTTON_WIDTH * 0.5f;
        float by = cy - 30f;

        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(bx, by));
        if (ImGui.Button("Start", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();
            mWorldsLoaded = false;
            mSelectedWorld = -1;
            mCurrentState = MainMenuState.WorldSelection;
        }

        PopBtn();

        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(bx, by + BUTTON_HEIGHT + BUTTON_SPACING));
        if (ImGui.Button("Options", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();
            mCurrentState = MainMenuState.Options;
        }

        PopBtn();

        PushRedBtn();
        ImGui.SetCursorPos(new Vector2(bx, by + (BUTTON_HEIGHT + BUTTON_SPACING) * 2));
        if (ImGui.Button("Quit", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();
            OnTitleQuitGame?.Invoke();
        }

        PopBtn();

        // Controls (bottom-left)
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        string[] controls =
        {
            $"{KeyName(Keybindings.MoveForward)} {KeyName(Keybindings.MoveBack)} {KeyName(Keybindings.MoveLeft)} {KeyName(Keybindings.MoveRight)}  -  Move",
            "LMB  -  Break / Attack",
            "RMB  -  Place block",
            "0-9  -  Select block",
            $"{KeyName(Keybindings.ResetPosition)}  -  Reset position",
            $"{KeyName(Keybindings.Wireframe)}  -  Wireframe",
            "Esc  -  Pause",
            $"{KeyName(Keybindings.ToggleCursor)}  -  Toggle cursor",
            $"{KeyName(Keybindings.ToggleFly)}  -  Fly / Instant break",
            $"{KeyName(Keybindings.Inventory)}  -  Inventory",
            $"{KeyName(Keybindings.Jump)}  -  Jump / Fly up",
            $"{KeyName(Keybindings.FlyDown)}  -  Fly down",
            $"{KeyName(Keybindings.Sprint)}  -  Sprint",
            $"{KeyName(Keybindings.RenderDistUp)}/{KeyName(Keybindings.RenderDistDown)}  -  Render distance",
            $"{KeyName(Keybindings.Screenshot)}  -  Take screenshot",
        };
        float lineH = ImGui.GetTextLineHeightWithSpacing();
        float totalH = controls.Length * lineH;
        float startY = windowSize.Y - totalH - 14f;

        for (int i = 0; i < controls.Length; i++)
        {
            ImGui.SetCursorPos(new Vector2(16f, startY + i * lineH));
            ImGui.Text(controls[i]);
        }

        ImGui.PopStyleColor();

        ImGui.End();
    }
}
