// Main menu class, holds stuff related to rendering the main menu. | DA | 2/5/26 Updated to look better, include save selection, and support keybinding customisation.

using ImGuiNET;
using System;
using System.Numerics;
using Silk.NET.Input;
using SilkKey = Silk.NET.Input.Key;
using VoxelEngine.Core;
using VoxelEngine.Saving;

namespace VoxelEngine.UI;

/// <summary>
/// Top-level main menu screen shown at boot and whenever the player returns to the menu. This partial class holds shared state (theme colors, layout constants, world-creation/ keybinding buffers) and small drawing/input helpers used by the various sub-screens; each sub-screen's actual rendering lives in its own partial file (<c>MainMenuScreen.Title.cs</c>, <c>.WorldSelection.cs</c>, <c>.NewGame.cs</c>, <c>.Options.cs</c>, <c>.Keybindings.cs</c>). <see cref="Render"/> dispatches to the right sub-screen based on <see cref="mCurrentState"/>.
/// </summary>
public partial class MainMenuScreen
{
    /// <summary>Which sub-screen of the main menu is currently active; drives the switch in <see cref="Render"/>.</summary>
    private enum MainMenuState
    {
        Title,
        WorldSelection,
        NewGame,
        Options,
        Keybindings
    }

    // World size input is clamped/rounded to an even number within this range (see NewGame screen).
    private const int MIN_WORLD_SIZE = 8;
    private const int MAX_WORLD_SIZE = 4096;

    // Layout - shared sizing constants so every sub-screen's buttons/panels line up consistently.
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

    // Display labels for world generation dropdowns in the New Game screen; index corresponds to the value stored in mWorldType/mWorldTheme, not a WorldGenSettings enum directly.
    private static readonly string[] WorldTypes = ["Island", "Inland", "Floating", "Flat"];
    private static readonly string[] WorldThemes = ["Normal", "Hell", "Paradise", "Woods"];

    // Ordered list of (display label, action) pairs driving the Keybindings screen's rows; order here is the order they're rendered in, not declaration order in Keybindings.Action.
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

    // New Game form state (indices into WorldTypes/WorldThemes for the two int fields).
    private int mWorldSize = 64;
    private int mWorldType = 0;
    private int mWorldTheme = 0;
    // Options screen slider state (0-100), mirrored into AudioManager on change.
    private int mVolSfx = 85;
    private int mVolMusic = 25;
    private bool mIsCreative = false;
    private bool mAsciiEnabled = false;

    private MainMenuState mCurrentState;

    /// <summary>Raised when the player quits from the title screen; Game.cs closes the application.</summary>
    public event Action OnTitleQuitGame = null!;
    /// <summary>
    /// Raised when the New Game / world-load flow finishes and gameplay should start. Parameters, in order: world size, SFX volume, music volume, world type index (into <see cref="WorldTypes"/>), world theme index (into <see cref="WorldThemes"/>), and whether creative mode is enabled. Game.cs wires this up to actually build/load the World and switch GameState to Playing.
    /// </summary>
    public event Action<int, int, int, int, int, bool> OnStartGame = null!;

    // Random flavor text shown under the title, Minecraft-style; one is picked per menu visit.
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

    // Index into BindingDefs currently waiting for a key press on the Keybindings screen; -1 means no rebind is in progress.
    private int mRebindingIndex = -1;

    public MainMenuScreen()
    {
        mCurrentSplash = mSplashText[Game.Instance.GameRandom.Next(0, mSplashText.Count)];
        mCurrentState = MainMenuState.Title;
        SetInputBuffer(mWorldNameBuffer, "New World");
    }

    /// <summary>
    /// Draws the fullscreen menu window and dispatches to whichever sub-screen is active, wrapped in the shared green theme (<see cref="PushTheme"/>/<see cref="PopTheme"/>). Called once per frame while GameState.MainMenu is active.
    /// </summary>
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

    /// <summary>Resets menu state back to the Title screen, e.g. after returning from a died/quit game. Also re-rolls the splash text.</summary>
    public void ResetToTitle()
    {
        mCurrentState = MainMenuState.Title;
        mSelectedWorld = -1;
        mWorldsLoaded = false;
        mShowDeleteConfirm = false;
        mCurrentSplash = mSplashText[Game.Instance.GameRandom.Next(0, mSplashText.Count)];
    }

    /// <summary>Re-scans the saves directory for available worlds; called when entering World Selection.</summary>
    public void RefreshWorldList()
    {
        mAvailableWorlds = Serialization.GetAllWorlds();
    }

    // Plays the standard UI click SFX at the current SFX volume setting; used by nearly every button.
    private void ClickSound()
    {
        Game.Instance.AudioManager.PlayAudio("Resources/Audio/UI/Click1.ogg", mVolSfx);
    }

    // Maps a Silk.NET key to a short display string for the Keybindings screen; falls back to the enum's ToString() for keys that don't need a friendlier label.
    private string KeyName(SilkKey key) => key switch
    {
        SilkKey.ShiftLeft => "L Shift",
        SilkKey.ShiftRight => "R Shift",
        SilkKey.ControlLeft => "L Ctrl",
        SilkKey.ControlRight => "R Ctrl",
        SilkKey.AltRight => "R Alt",
        SilkKey.AltLeft => "L Alt",
        SilkKey.Space => "Space",
        SilkKey.Tab => "Tab",
        SilkKey.Escape => "Esc",
        SilkKey.Enter => "Enter",
        SilkKey.Equal => "+",
        SilkKey.Minus => "-",
        _ => key.ToString()
    };

    // Drawing helpers

    // Draws a large, centered, green title at a fixed Y near the top of the window - used at the top of every sub-screen for a consistent look.
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

    // Draws a rounded, bordered background panel directly via the window's draw list (rather than a child window) so form fields can be freely positioned with SetCursorPos on top of it. Coordinates are window-local; converted to absolute screen space via the window's position.
    private void DrawPanel(float x, float y, float w, float h)
    {
        var drawList = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var min = new Vector2(winPos.X + x, winPos.Y + y);
        var max = new Vector2(min.X + w, min.Y + h);

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(ColPanel), ROUNDING);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(ColPanelBorder), ROUNDING, ImDrawFlags.None, 1f);
    }

    // Button style helpers These push/pop matched sets of ImGui style colors around Button() calls so different buttons (confirm vs destructive vs disabled) get distinct look without a full ImGui theme.

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

    // Pops the 3 colors pushed by PushGreenBtn/PushRedBtn. Caller must match push/pop calls - mismatches here corrupt ImGui's style stack for the rest of the frame.
    private void PopBtn()
    {
        ImGui.PopStyleColor(3);
    }

    // Like PushGreenBtn/PushRedBtn but greys the button out and disables its text color when `enabled` is false (visual only - callers are still responsible for skipping the click logic themselves; ImGui doesn't disable input here).
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

    // Pops the colors pushed by PushDisableableBtn; count differs (3 vs 4) depending on whether the disabled branch pushed the extra Text color, so `enabled` must match the paired push call.
    private void PopDisableableBtn(bool enabled, bool _)
    {
        ImGui.PopStyleColor(enabled ? 3 : 4);
    }

    // Theme

    // Pushes the entire green-themed ImGui color/style-var palette used across all main menu sub-screens. Must be paired 1:1 with PopTheme (which pops the exact same counts) - called once per Render() around the sub-screen dispatch switch.
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

    // Counts (7 style vars, 19 colors) must exactly match the number of PushStyleVar/ PushStyleColor calls in PushTheme, or ImGui's internal style stack will desync and corrupt styling for every screen rendered afterward this frame.
    private void PopTheme()
    {
        ImGui.PopStyleVar(7);
        ImGui.PopStyleColor(19);
    }

    // Input buffer helpers ImGui.InputText requires a raw fixed-size byte buffer (null-terminated UTF-8) rather than a managed string, so these helpers convert to/from that representation.

    // Fills `buffer` with the UTF-8 bytes of `value`, zero-clearing first so any leftover bytes from a previous longer string don't corrupt the null-terminated read.
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

    // Reads the buffer back out as a managed string, stopping at the first null byte (or using the whole buffer if ImGui filled it completely with no terminator).
    private string GetStringFromBuffer(byte[] buffer)
    {
        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length);
    }
}
