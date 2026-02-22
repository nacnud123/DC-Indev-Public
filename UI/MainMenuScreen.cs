// Main menu class, holds stuff related to rendering the main menu. | DA | 2/5/26
// Updated to look better and include save selection.
using ImGuiNET;
using System;
using System.Numerics;
using VoxelEngine.Core;
using VoxelEngine.Saving;

namespace VoxelEngine.UI;

public class MainMenuScreen
{
    private enum MainMenuState
    {
        Title,
        WorldSelection,
        NewGame,
        Options
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

    private int mWorldSize = 64;
    private int mWorldType = 0;
    private int mWorldTheme = 0;
    private int mVolSfx = 85;
    private int mVolMusic = 25;

    private MainMenuState mCurrentState;

    public event Action OnTitleQuitGame = null!;
    public event Action<int, int, int, int, int> OnStartGame = null!;

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
        "Will it ever leave Alpha? No",
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
            default:
                throw new ArgumentOutOfRangeException();
        }

        PopTheme();
    }

    // Title Screen

    private void RenderTitleScreen(ImGuiWindowFlags windowFlags)
    {
        ImGui.Begin("TitleScreenMainMenu", windowFlags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;
        var cy = windowSize.Y * 0.5f;

        // Version (top-left)
        ImGui.SetCursorPos(new Vector2(12, 8));
        ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
        ImGui.Text("Version 6.0A");
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
            "W A S D  -  Move",
            "LMB  -  Break / Attack",
            "RMB  -  Place block",
            "0-9  -  Select block",
            "R  -  Reset position",
            "X  -  Wireframe",
            "P  -  Spawn pig",
            "Esc  -  Pause",
            "Tab  -  Toggle cursor",
            "F  -  Fly / Instant break",
            "E  -  Inventory",
            "Space  -  Jump / Fly up",
            "Ctrl  -  Fly down",
            "Shift  -  Sprint",
            "+/-  -  Render distance",
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

    // World Selection
    private void RenderWorldSelectionScreen(ImGuiWindowFlags flags)
    {
        if (!mWorldsLoaded)
        {
            RefreshWorldList();
            mWorldsLoaded = true;
        }

        ImGui.Begin("WorldSelectionMenu", flags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;

        // Title
        DrawTitle("Select World", cx);

        // World list panel
        float listW = MathF.Min(600f, windowSize.X - 80f);
        float listH = windowSize.Y - 220f;
        float listX = cx - listW * 0.5f;
        float listY = 90f;

        // Panel background
        DrawPanel(listX, listY, listW, listH);

        ImGui.SetCursorPos(new Vector2(listX + 1, listY + 1));
        ImGui.BeginChild("WorldList", new Vector2(listW - 2, listH - 2), ImGuiChildFlags.None);

        if (mAvailableWorlds.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ColTextMuted);
            var noText = "No worlds found";
            var ntSize = ImGui.CalcTextSize(noText);
            ImGui.SetCursorPos(new Vector2((listW - 2) * 0.5f - ntSize.X * 0.5f, (listH - 2) * 0.5f - ntSize.Y * 0.5f));
            ImGui.Text(noText);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

            for (int i = 0; i < mAvailableWorlds.Count; i++)
            {
                var world = mAvailableWorlds[i];
                string lastPlayed = world.LastPlayed != DateTime.MinValue ? world.LastPlayed.ToString("yyyy-MM-dd HH:mm") : "Unknown";

                bool selected = mSelectedWorld == i;

                // Alternate row shading
                if (i % 2 == 0)
                {
                    var drawList = ImGui.GetWindowDrawList();
                    var cursorScreen = ImGui.GetCursorScreenPos();
                    drawList.AddRectFilled(cursorScreen, new Vector2(cursorScreen.X + listW - 20f, cursorScreen.Y + 48f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.08f, 0.06f, 0.5f)));
                }

                if (ImGui.Selectable($"##world_{i}", selected, ImGuiSelectableFlags.None, new Vector2(0, 48)))
                {
                    mSelectedWorld = i;
                }

                // Draw world name and date on top of the selectable
                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();
                var dl = ImGui.GetWindowDrawList();

                dl.AddText(new Vector2(itemMin.X + 12f, itemMin.Y + 6f), ImGui.ColorConvertFloat4ToU32(ColText), world.WorldName);
                dl.AddText(new Vector2(itemMin.X + 12f, itemMin.Y + 26f), ImGui.ColorConvertFloat4ToU32(ColTextDim), $"Last played: {lastPlayed} - {world.WorldSize}x{world.WorldSize}");

                // Thin separator line
                if (i < mAvailableWorlds.Count - 1)
                {
                    dl.AddLine(new Vector2(itemMin.X + 8f, itemMax.Y), new Vector2(itemMax.X - 8f, itemMax.Y), ImGui.ColorConvertFloat4ToU32(ColSeparator), 1f);
                }
            }

            ImGui.PopStyleVar();
        }

        ImGui.EndChild();

        // DEL key
        bool hasSel = mSelectedWorld >= 0 && mSelectedWorld < mAvailableWorlds.Count;
        
        if (hasSel && ImGui.IsKeyPressed(ImGuiKey.Delete))
            mShowDeleteConfirm = true;

        // Bottom buttons
        float btnY = windowSize.Y - 62f;
        float totalW = BUTTON_WIDTH * 4 + BUTTON_SPACING * 3;
        float bx = cx - totalW * 0.5f;

        // Play
        PushDisableableBtn(hasSel, false);
        ImGui.SetCursorPos(new Vector2(bx, btnY));
        if (ImGui.Button("Play", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)) && hasSel)
        {
            ClickSound();
            Serialization.s_WorldName = mAvailableWorlds[mSelectedWorld].WorldName;
            OnStartGame?.Invoke(Serialization.GetWorldSize(mAvailableWorlds[mSelectedWorld].WorldName), mVolSfx, mVolMusic, 0, 0);
        }
        PopDisableableBtn(hasSel, false);

        // Delete
        PushDisableableBtn(hasSel, true);
        ImGui.SetCursorPos(new Vector2(bx + (BUTTON_WIDTH + BUTTON_SPACING), btnY));
        if (ImGui.Button("Delete", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)) && hasSel)
        {
            ClickSound();
            mShowDeleteConfirm = true;
        }
        PopDisableableBtn(hasSel, true);

        // New World
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(bx + (BUTTON_WIDTH + BUTTON_SPACING) * 2, btnY));
        if (ImGui.Button("New World", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();
            mWorldSize = 16;
            mWorldType = 0;
            mWorldTheme = 0;
            SetInputBuffer(mWorldNameBuffer, "New World");
            mCurrentState = MainMenuState.NewGame;
        }
        PopBtn();

        // Back
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(bx + (BUTTON_WIDTH + BUTTON_SPACING) * 3, btnY));
        if (ImGui.Button("Back", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();
            mCurrentState = MainMenuState.Title;
        }
        PopBtn();

        // Delete confirmation popup
        if (mShowDeleteConfirm && hasSel)
        {
            ImGui.OpenPopup("Delete World?");
            mShowDeleteConfirm = false;
        }

        RenderDeletePopup(cx, windowSize.Y);

        ImGui.End();
    }

    private void RenderDeletePopup(float cx, float winH)
    {
        bool hasSel = mSelectedWorld >= 0 && mSelectedWorld < mAvailableWorlds.Count;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, ROUNDING);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ColPanel);
        ImGui.PushStyleColor(ImGuiCol.Border, ColPanelBorder);
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);

        if (ImGui.BeginPopupModal("Delete World?", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            var popupW = 360f;
            ImGui.SetWindowSize(new Vector2(popupW, 0));
            ImGui.SetWindowPos(new Vector2(cx - popupW * 0.5f, winH * 0.5f - 70f));

            string name = hasSel ? mAvailableWorlds[mSelectedWorld].WorldName : "";
            ImGui.Text($"Delete \"{name}\"?");
            ImGui.PushStyleColor(ImGuiCol.Text, ColTextDim);
            ImGui.Text("This cannot be undone.");
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Spacing();

            float bw = (popupW - 40f - BUTTON_SPACING) * 0.5f;

            PushRedBtn();
            if (ImGui.Button("Delete", new Vector2(bw, 36)))
            {
                ClickSound();
                Serialization.DeleteWorld(name);
                mSelectedWorld = -1;
                mWorldsLoaded = false;
                RefreshWorldList();
                mWorldsLoaded = true;
                ImGui.CloseCurrentPopup();
            }
            PopBtn();

            ImGui.SameLine(0, BUTTON_SPACING);

            PushGreenBtn();
            if (ImGui.Button("Cancel", new Vector2(bw, 36)))
            {
                ImGui.CloseCurrentPopup();
            }
            PopBtn();

            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }

    // New Game

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
                      + labelH + inputH + rowGap // Name
                      + BUTTON_HEIGHT + rowGap // Type
                      + labelH + inputH + rowGap // Size
                      + BUTTON_HEIGHT + bigGap // Theme + large gap before actions
                      + BUTTON_HEIGHT + rowGap // Create
                      + BUTTON_HEIGHT // Cancel
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
            Serialization.s_WorldName = worldName;
            Serialization.CreateWorld(worldName, null, mWorldSize, mWorldType, mWorldTheme);
            OnStartGame?.Invoke(mWorldSize, mVolSfx, mVolMusic, mWorldType, mWorldTheme);
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

    // Options
    private void RenderOptionsScreen(ImGuiWindowFlags flags)
    {
        ImGui.Begin("OptionsMenu", flags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;
        var cy = windowSize.Y * 0.5f;

        DrawTitle("Options", cx);

        // Panel
        float formW = 440f;
        float formH = 180f;
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

        ImGui.PopStyleColor();

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
    private static void PushGreenBtn()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, BtnGreen);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnGreenHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, BtnGreenActive);
    }

    private static void PushRedBtn()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, BtnRed);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnRedHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, BtnRedActive);
    }

    private static void PopBtn()
    {
        ImGui.PopStyleColor(3);
    }

    private static void PushDisableableBtn(bool enabled, bool red)
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

    private static void PopDisableableBtn(bool enabled, bool _)
    {
        ImGui.PopStyleColor(enabled ? 3 : 4);
    }

    // Theme
    private static void PushTheme()
    {
        // Window
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ColBg);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Border, ColPanelBorder);

        // Frames (input fields, sliders)
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ColFrame);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ColFrameHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ColFrameActive);

        // Slider grab
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, ColSliderGrab);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ColSliderGrabAct);

        // Check / radio
        ImGui.PushStyleColor(ImGuiCol.CheckMark, ColCheck);

        // Header (selectable)
        ImGui.PushStyleColor(ImGuiCol.Header, ColHeader);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ColHeaderHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ColHeaderActive);

        // Scrollbar
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, ColScrollbar);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, ColScrollbarGrab);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, ColScrollbarHover);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, ColScrollbarAct);

        // Separator
        ImGui.PushStyleColor(ImGuiCol.Separator, ColSeparator);

        // Text
        ImGui.PushStyleColor(ImGuiCol.Text, ColText);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, ColTextDim);

        // Rounding
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, ROUNDING);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);

        // Padding
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 6));
    }

    private static void PopTheme()
    {
        ImGui.PopStyleVar(7);
        ImGui.PopStyleColor(19);
    }

    // Input buffer helpers
    private static void SetInputBuffer(byte[] buffer, string value)
    {
        Array.Clear(buffer, 0, buffer.Length);
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, buffer.Length - 1));
    }

    private static void ClearInputBuffer(byte[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
    }

    private static string GetStringFromBuffer(byte[] buffer)
    {
        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length);
    }
}
