// Main menu class, holds stuff related to rendering the main menu. | DA | 2/5/26
// Updated to look better, include save selection, and support keybinding customisation.

using ImGuiNET;
using System;
using System.Numerics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VoxelEngine.Core;
using VoxelEngine.Saving;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    private enum MainMenuState
    {
        Title,
        WorldSelection,
        NewGame,
        Options,
        Keybindings
    }

    private const int MIN_WORLD_SIZE = 8;
    private const int MAX_WORLD_SIZE = 4096;

    // Layout
    private const float BUTTON_WIDTH = 220f;
    private const float BUTTON_HEIGHT = 42f;
    private const float BUTTON_SPACING = 8f;
    private const float ROUNDING = 6f;
    private const float PANEL_PAD = 20f;

    // Colors - green theme
    private static readonly Vector4 ColBg = new(0.04f, 0.04f, 0.04f, 1f);
    private static readonly Vector4 ColText = new(0.25f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 ColTextDim = new(0.18f, 0.5f, 0.18f, 1f);
    private static readonly Vector4 ColTextMuted = new(0.4f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 ColPanel = new(0.08f, 0.10f, 0.08f, 1f);
    private static readonly Vector4 ColPanelBorder = new(0.15f, 0.35f, 0.15f, 0.6f);

    // Green button
    private static readonly Vector4 BtnGreen = new(0.12f, 0.42f, 0.12f, 1f);
    private static readonly Vector4 BtnGreenHover = new(0.16f, 0.55f, 0.16f, 1f);
    private static readonly Vector4 BtnGreenActive = new(0.08f, 0.35f, 0.08f, 1f);

    // Red button
    private static readonly Vector4 BtnRed = new(0.55f, 0.12f, 0.12f, 1f);
    private static readonly Vector4 BtnRedHover = new(0.70f, 0.18f, 0.18f, 1f);
    private static readonly Vector4 BtnRedActive = new(0.45f, 0.08f, 0.08f, 1f);

    // Disabled
    private static readonly Vector4 BtnDisabled = new(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Vector4 ColDisabledText = new(0.35f, 0.35f, 0.35f, 1f);

    // Frames / widgets
    private static readonly Vector4 ColFrame = new(0.10f, 0.14f, 0.10f, 1f);
    private static readonly Vector4 ColFrameHover = new(0.14f, 0.22f, 0.14f, 1f);
    private static readonly Vector4 ColFrameActive = new(0.10f, 0.30f, 0.10f, 1f);
    private static readonly Vector4 ColSliderGrab = new(0.20f, 0.60f, 0.20f, 1f);
    private static readonly Vector4 ColSliderGrabAct = new(0.25f, 0.75f, 0.25f, 1f);
    private static readonly Vector4 ColCheck = new(0.20f, 0.65f, 0.20f, 1f);
    private static readonly Vector4 ColHeader = new(0.12f, 0.30f, 0.12f, 0.6f);
    private static readonly Vector4 ColHeaderHover = new(0.15f, 0.40f, 0.15f, 0.8f);
    private static readonly Vector4 ColHeaderActive = new(0.10f, 0.50f, 0.10f, 1f);
    private static readonly Vector4 ColScrollbar = new(0.06f, 0.08f, 0.06f, 1f);
    private static readonly Vector4 ColScrollbarGrab = new(0.18f, 0.35f, 0.18f, 1f);
    private static readonly Vector4 ColScrollbarHover = new(0.22f, 0.45f, 0.22f, 1f);
    private static readonly Vector4 ColScrollbarAct = new(0.25f, 0.55f, 0.25f, 1f);
    private static readonly Vector4 ColSeparator = new(0.15f, 0.30f, 0.15f, 0.4f);

    private static readonly string[] WorldTypes = ["Island", "Inland", "Floating", "Flat"];
    private static readonly string[] WorldThemes = ["Normal", "Hell", "Paradise", "Woods"];

    private static readonly (string Label, Keybindings.Action Binding)[] BindingDefs =
    [
        ("Move Forward", Keybindings.Action.MoveForward),
        ("Move Back", Keybindings.Action.MoveBack),
        ("Move Left", Keybindings.Action.MoveLeft),
        ("Move Right", Keybindings.Action.MoveRight),
        ("Jump", Keybindings.Action.Jump),
        ("Sprint", Keybindings.Action.Sprint),
        ("Fly Down", Keybindings.Action.FlyDown),
        ("Toggle Fly", Keybindings.Action.ToggleFly),
        ("Inventory", Keybindings.Action.Inventory),
        ("Drop Item", Keybindings.Action.DropItem),
        ("Wireframe", Keybindings.Action.Wireframe),
        ("Reset Position", Keybindings.Action.ResetPosition),
        ("Toggle Cursor", Keybindings.Action.ToggleCursor),
        ("Screenshot", Keybindings.Action.Screenshot),
        ("Render Dist Up", Keybindings.Action.RenderDistUp),
        ("Render Dist Down", Keybindings.Action.RenderDistDown),
    ];

    private int mWorldSize = 64;
    private int mWorldType = 0;
    private int mWorldTheme = 0;
    private int mVolSfx = 85;
    private int mVolMusic = 25;
    private bool mIsCreative = false;
    private bool mAsciiEnabled = false;

    private MainMenuState mCurrentState;

    public event Action OnTitleQuitGame = null!;
    public event Action<int, int, int, int, int, bool> OnStartGame = null!;

    private List<string> mSplashText = new()
    {
        "Try not to crash!",
        "Pigs scare me",
        "Don't forget to eat your vegetables",
        "Hi Dillon",
        "Also try DuncanCraft 2000!",
        "Microsoft don't sue",
        "Minecraft but worse",
        "New and original game",
        "Spong",
        "Hire me",
        "This is a splash text",
        "I hope you like it",
        "Star the GitHub repo",
        "Also try Project Soup!",
        "DA_RL on steam soon",
        "Thanks ARoachIFoundOnMyPillow for textures!",
        "Thanks TheQuantumBlaze for textures!",
        "Will it ever leave Alpha? Maybe",
        "Watch out for bugs!",
        "No cows",
        "Rip other DuncanCraft projects",
        "All the animals move at the same time",
        "It's a feature not a bug",
        "I have no idea what I'm doing"
    };

    private string mCurrentSplash;

    private int mSelectedWorld = -1;
    private bool mWorldsLoaded = false;
    private bool mShowDeleteConfirm = false;
    private readonly byte[] mWorldNameBuffer = new byte[256];
    private List<WorldSaveData> mAvailableWorlds = new();

    private int mRebindingIndex = -1;

    public MainMenuScreen()
    {
        mCurrentSplash = mSplashText[Game.Instance.GameRandom.Next(0, mSplashText.Count)];
        mCurrentState = MainMenuState.Title;
        SetInputBuffer(mWorldNameBuffer, "New World");
    }

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

        PushTheme();

        switch (mCurrentState)
        {
            case MainMenuState.Title:
                RenderTitleScreen(windowFlags);
                break;
            case MainMenuState.WorldSelection:
                RenderWorldSelectionScreen(windowFlags);
                break;
            case MainMenuState.NewGame:
                RenderNewGameScreen(windowFlags);
                break;
            case MainMenuState.Options:
                RenderOptionsScreen(windowFlags);
                break;
            case MainMenuState.Keybindings:
                RenderKeybindingsScreen(windowFlags);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        PopTheme();
    }

    // Helpers

    public void ResetToTitle()
    {
        mCurrentState = MainMenuState.Title;
        mSelectedWorld = -1;
        mWorldsLoaded = false;
        mShowDeleteConfirm = false;
        mCurrentSplash = mSplashText[Game.Instance.GameRandom.Next(0, mSplashText.Count)];
    }

    public void RefreshWorldList()
    {
        mAvailableWorlds = Serialization.GetAllWorlds();
    }

    private void ClickSound()
    {
        Game.Instance.AudioManager.PlayAudio("Resources/Audio/UI/Click1.ogg", mVolSfx);
    }

    private string KeyName(Keys key) => key switch
    {
        Keys.LeftShift => "L Shift",
        Keys.RightShift => "R Shift",
        Keys.LeftControl => "L Ctrl",
        Keys.RightControl => "R Ctrl",
        Keys.RightAlt => "R Alst",
        Keys.LeftAlt => "L Alt",
        Keys.Space => "Space",
        Keys.Tab => "Tab",
        Keys.Escape => "Esc",
        Keys.Enter => "Enter",
        Keys.Equal => "+",
        Keys.Minus => "-",
        _ => key.ToString()
    };

    // Drawing helpers

    private void DrawTitle(string text, float cx)
    {
        ImGui.PushFont(ImGuiController.fontLarge);
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPos(new Vector2(cx - size.X * 0.5f, 30f));
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.Text(text);
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    private void DrawPanel(float x, float y, float w, float h)
    {
        var drawList = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var min = new Vector2(winPos.X + x, winPos.Y + y);
        var max = new Vector2(min.X + w, min.Y + h);

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(ColPanel), ROUNDING);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(ColPanelBorder), ROUNDING, ImDrawFlags.None, 1f);
    }

    // Button style helpers

    private void PushGreenBtn()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, BtnGreen);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnGreenHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, BtnGreenActive);
    }

    private void PushRedBtn()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, BtnRed);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnRedHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, BtnRedActive);
    }

    private void PopBtn()
    {
        ImGui.PopStyleColor(3);
    }

    private void PushDisableableBtn(bool enabled, bool red)
    {
        if (enabled)
        {
            if (red) PushRedBtn();
            else PushGreenBtn();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, BtnDisabled);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnDisabled);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, BtnDisabled);
            ImGui.PushStyleColor(ImGuiCol.Text, ColDisabledText);
        }
    }

    private void PopDisableableBtn(bool enabled, bool _)
    {
        ImGui.PopStyleColor(enabled ? 3 : 4);
    }

    // Theme

    private void PushTheme()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ColBg);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Border, ColPanelBorder);

        ImGui.PushStyleColor(ImGuiCol.FrameBg, ColFrame);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ColFrameHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ColFrameActive);

        ImGui.PushStyleColor(ImGuiCol.SliderGrab, ColSliderGrab);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ColSliderGrabAct);

        ImGui.PushStyleColor(ImGuiCol.CheckMark, ColCheck);

        ImGui.PushStyleColor(ImGuiCol.Header, ColHeader);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ColHeaderHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ColHeaderActive);

        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, ColScrollbar);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, ColScrollbarGrab);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, ColScrollbarHover);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, ColScrollbarAct);

        ImGui.PushStyleColor(ImGuiCol.Separator, ColSeparator);

        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, ColTextDim);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 6));
    }

    private void PopTheme()
    {
        ImGui.PopStyleVar(7);
        ImGui.PopStyleColor(19);
    }

    // Input buffer helpers

    private void SetInputBuffer(byte[] buffer, string value)
    {
        Array.Clear(buffer, 0, buffer.Length);
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, buffer.Length - 1));
    }

    private void ClearInputBuffer(byte[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
    }

    private string GetStringFromBuffer(byte[] buffer)
    {
        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length);
    }
}
