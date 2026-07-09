using ImGuiNET;
using System;
using System.Numerics;
using Silk.NET.Input;
using SilkKey = Silk.NET.Input.Key;
using VoxelEngine.Core;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
    /// <summary>
    /// Renders the key rebinding grid (two columns, driven by <see cref="BindingDefs"/>) and handles entering/exiting "waiting for key press" mode via <see cref="PollRebindKey"/>. Changes are written live to <c>Keybindings</c> and persisted to disk when the player clicks Back. Only called while <c>mCurrentState == MainMenuState.Keybindings</c>.
    /// </summary>
    private void RenderKeybindingsScreen(ImGuiWindowFlags flags)
    {
        ImGui.Begin("KeybindingsMenu", flags);

        var windowSize = ImGui.GetWindowSize();
        var cx = windowSize.X * 0.5f;
        var cy = windowSize.Y * 0.5f;

        DrawTitle("Keybindings", cx);

        const float ROW_H = 40f;
        const int COLS = 2;
        // Bindings fill column 0 top-to-bottom, then column 1 (see the `i = col * numRows + row` indexing below) - so the grid reads like two independently-filled lists side by side.
        int numRows = (BindingDefs.Length + COLS - 1) / COLS;
        float panelW = 760f;
        float colW = (panelW - 2f) / COLS;
        float panelH = numRows * ROW_H + PANEL_PAD * 2;
        float panelX = cx - panelW * 0.5f;
        float panelY = cy - panelH * 0.5f - 20f;

        DrawPanel(panelX, panelY, panelW, panelH);

        // If a rebind is in progress, check every frame for a key press before drawing the rows below, so this frame's row already reflects "[Press any key]" / the new key.
        if (mRebindingIndex >= 0)
            PollRebindKey();

        // Manually laid out via raw draw-list text/rects rather than ImGui's table API, so the whole two-column list can share one child window and one Selectable per binding row.
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

                // Alternating row shading (zebra stripes) for readability; the row currently being rebound always gets a highlighted background regardless of parity.
                if (isRebinding || row % 2 == 0)
                {
                    var bgPos = new Vector2(childOrigin.X + itemX, childOrigin.Y + itemY);
                    uint bgColor = isRebinding
                        ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.40f, 0.10f, 0.8f))
                        : ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.08f, 0.06f, 0.5f));
                    childDrawList.AddRectFilled(bgPos, new Vector2(bgPos.X + colW, bgPos.Y + ROW_H), bgColor);
                }

                // Invisible-labeled Selectable spanning the row gives click/hover handling; the actual label/key text is drawn manually below via the draw list. Clicking the row currently being rebound cancels it; clicking any other row starts rebinding it (only one row can be "armed" at a time).
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

        // Back - cancels any pending rebind, persists all bindings to disk, and returns to Options.
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

    /// <summary>
    /// Scans every Silk.NET key for a fresh press while a rebind is armed (<see cref="mRebindingIndex"/> &gt;= 0). Escape cancels without changing anything; any other key becomes the new binding for the armed action. If that key was already bound to a different action, the two bindings are swapped (the other action inherits the key the armed action used to have) so no two actions can ever end up sharing the same key.
    /// </summary>
    private void PollRebindKey()
    {
        foreach (SilkKey k in Enum.GetValues<SilkKey>())
        {
            if (k == SilkKey.Unknown || !Game.Instance.IsKeyPressed(k))
                continue;

            if (k != SilkKey.Escape)
            {
                var target = BindingDefs[mRebindingIndex].Binding;
                SilkKey oldKey = Keybindings.Get(target);

                // If another action already uses the newly-pressed key, give it the key the target action is vacating instead of leaving a duplicate binding.
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

            // Whether accepted or cancelled (Escape), exit rebind mode after the first key seen.
            mRebindingIndex = -1;
            break;
        }
    }
}
