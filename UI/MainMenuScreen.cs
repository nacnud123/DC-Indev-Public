// Main main menu class, holds stuff related to rendering the main menu. | DA | 2/5/26
using ImGuiNET;
using System;
using System.Numerics;
using VoxelEngine.Core;

namespace VoxelEngine.UI;
public class MainMenuScreen
{
    private const int MIN_WORLD_SIZE = 8;
    private const int MAX_WORLD_SIZE = 4096;

    private int mWorldSize = 8;
    private int mVolSFX = 85;
    private int mVolMusic = 25;

    public event Action OnTitleQuitGame;
    public event Action<int, int, int> OnStartGame;

    private List<string> SplashText = new()
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

    string currentSplash = "";

    public MainMenuScreen()
    {
        currentSplash = SplashText[Game.Instance.GameRandom.Next(0, SplashText.Count)];
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

        var spashText = currentSplash;

        var splashSize = ImGui.CalcTextSize(spashText);
        ImGui.SetCursorPos(new Vector2(centerX - splashSize.X * 0.5f, centerY - 90f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text(spashText);
        ImGui.PopStyleColor();

        // Version text
        ImGui.PopStyleColor();

        ImGui.SetCursorPos(new Vector2(10, 10));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text("Version 5.0A");


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
            F = Toggle fly mode / instant break mode
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
        ImGui.SetCursorPos(new Vector2(centerX - 200f, centerY - 70f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text("World Size:");

        ImGui.SetCursorPos(new Vector2(centerX - 200f, centerY - 50f));
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

        // Volume Controls (same line)
        ImGui.SetCursorPos(new Vector2(centerX - 200f, centerY - 15f));
        ImGui.Text("SFX:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        
        if (ImGui.InputInt("##sfxvolume", ref mVolSFX))
            mVolSFX = Math.Clamp(mVolSFX, 0, 100);
        
        ImGui.SameLine();
        ImGui.Text("Music:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        
        if (ImGui.InputInt("##musicvolume", ref mVolMusic))
            mVolMusic = Math.Clamp(mVolMusic, 0, 100);

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
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/UI/Click1.ogg", mVolSFX);
            OnStartGame?.Invoke(mWorldSize, mVolSFX, mVolMusic);
        }

        // Quit button
        ImGui.SetCursorPos(new Vector2(buttonStartX + buttonWidth + buttonSpacing, buttonY));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.1f, 0.1f, 1.0f));

        if (ImGui.Button("Quit", new Vector2(buttonWidth, buttonHeight)))
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/UI/Click1.ogg", mVolSFX);
            OnTitleQuitGame?.Invoke();
        }

        ImGui.PopStyleColor(3);

        ImGui.End();

        ImGui.PopStyleColor();
    }
}