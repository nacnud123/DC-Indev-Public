using ImGuiNET;
using System;
using System.Numerics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VoxelEngine.Core;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    private void RenderKeybindingsScreen(ImGuiWindowFlags flags)
    {
        ImGui.Begin("KeybindingsMenu", flags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;
        var cy = windowSize.Y * 0.5f;

        DrawTitle("Keybindings", cx);

        const float ROW_H = 40f;
        const int COLS = 2;
        int numRows = (BindingDefs.Length + COLS - 1) / COLS;
        float panelW = 760f;
        float colW = (panelW - 2f) / COLS;
        float panelH = numRows * ROW_H + PANEL_PAD * 2;
        float panelX = cx - panelW * 0.5f;
        float panelY = cy - panelH * 0.5f - 20f;

        DrawPanel(panelX, panelY, panelW, panelH);

        if (mRebindingIndex >= 0)
            PollRebindKey();

        ImGui.SetCursorPos(new Vector2(panelX + 1, panelY + PANEL_PAD));
        ImGui.BeginChild("BindingList", new Vector2(panelW - 2, numRows * ROW_H), ImGuiChildFlags.None);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

        var childDrawList = ImGui.GetWindowDrawList();
        var childOrigin = ImGui.GetWindowPos();

        for (int row = 0; row < numRows; row++)
        {
            for (int col = 0; col < COLS; col++)
            {
                int i = col * numRows + row;
                if (i >= BindingDefs.Length)
                    continue;

                var (label, binding) = BindingDefs[i];
                bool isRebinding = mRebindingIndex == i;
                float itemX = col * colW;
                float itemY = row * ROW_H;

                if (isRebinding || row % 2 == 0)
                {
                    var bgPos = new Vector2(childOrigin.X + itemX, childOrigin.Y + itemY);
                    uint bgColor = isRebinding
                        ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.40f, 0.10f, 0.8f))
                        : ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.08f, 0.06f, 0.5f));
                    childDrawList.AddRectFilled(bgPos, new Vector2(bgPos.X + colW, bgPos.Y + ROW_H), bgColor);
                }

                ImGui.SetCursorPos(new Vector2(itemX, itemY));
                if (ImGui.Selectable($"##bind{i}", isRebinding, ImGuiSelectableFlags.None, new Vector2(colW, ROW_H)))
                {
                    ClickSound();
                    mRebindingIndex = isRebinding ? -1 : i;
                }

                var itemMin = ImGui.GetItemRectMin();
                float textY = itemMin.Y + (ROW_H - ImGui.GetTextLineHeight()) * 0.5f;
                string keyText = isRebinding ? "[Press any key]" : Keybindings.Get(binding).ToString();

                childDrawList.AddText(new Vector2(itemMin.X + 14f, textY),
                    ImGui.ColorConvertFloat4ToU32(ColText), label);
                childDrawList.AddText(new Vector2(itemMin.X + colW - 120f, textY),
                    ImGui.ColorConvertFloat4ToU32(isRebinding ? ColTextDim : ColText), keyText);
            }
        }

        uint divColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.08f));
        childDrawList.AddLine(new Vector2(childOrigin.X + colW, childOrigin.Y), new Vector2(childOrigin.X + colW, childOrigin.Y + numRows * ROW_H), divColor);

        ImGui.PopStyleVar();
        ImGui.EndChild();

        float backY = panelY + panelH + 20f;
        PushGreenBtn();
        ImGui.SetCursorPos(new Vector2(cx - BUTTON_WIDTH * 0.5f, backY));
        if (ImGui.Button("Back", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)))
        {
            ClickSound();
            mRebindingIndex = -1;
            Keybindings.Save();
            mCurrentState = MainMenuState.Options;
        }
        PopBtn();

        ImGui.End();
    }

    private void PollRebindKey()
    {
        var keyboard = Game.Instance.KeyboardState;
        foreach (Keys k in Enum.GetValues<Keys>())
        {
            if (k == Keys.Unknown || !keyboard.IsKeyPressed(k))
                continue;

            if (k != Keys.Escape)
            {
                var target = BindingDefs[mRebindingIndex].Binding;
                Keys oldKey = Keybindings.Get(target);

                for (int j = 0; j < BindingDefs.Length; j++)
                {
                    if (j != mRebindingIndex && Keybindings.Get(BindingDefs[j].Binding) == k)
                    {
                        Keybindings.Set(BindingDefs[j].Binding, oldKey);
                        break;
                    }
                }

                Keybindings.Set(target, k);
            }

            mRebindingIndex = -1;
            break;
        }
    }
}
