using ImGuiNET;
using System;
using System.Numerics;
using VoxelEngine.Saving;

namespace VoxelEngine.UI;

public partial class MainMenuScreen
{
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

        DrawTitle("Select World", cx);

        float listW = MathF.Min(600f, windowSize.X - 80f);
        float listH = windowSize.Y - 220f;
        float listX = cx - listW * 0.5f;
        float listY = 90f;

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

                if (i % 2 == 0)
                {
                    var drawList = ImGui.GetWindowDrawList();
                    var cursorScreen = ImGui.GetCursorScreenPos();
                    drawList.AddRectFilled(cursorScreen, new Vector2(cursorScreen.X + listW - 20f, cursorScreen.Y + 48f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.08f, 0.06f, 0.5f)));
                }

                if (ImGui.Selectable($"##world_{i}", selected, ImGuiSelectableFlags.None, new Vector2(0, 48)))
                    mSelectedWorld = i;

                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();
                var dl = ImGui.GetWindowDrawList();

                dl.AddText(new Vector2(itemMin.X + 12f, itemMin.Y + 6f), ImGui.ColorConvertFloat4ToU32(ColText), world.WorldName);
                dl.AddText(new Vector2(itemMin.X + 12f, itemMin.Y + 26f), ImGui.ColorConvertFloat4ToU32(ColTextDim), $"Last played: {lastPlayed} - {world.WorldSize}x{world.WorldSize}");

                if (i < mAvailableWorlds.Count - 1)
                {
                    dl.AddLine(new Vector2(itemMin.X + 8f, itemMax.Y), new Vector2(itemMax.X - 8f, itemMax.Y),
                        ImGui.ColorConvertFloat4ToU32(ColSeparator), 1f);
                }
            }

            ImGui.PopStyleVar();
        }

        ImGui.EndChild();

        bool hasSel = mSelectedWorld >= 0 && mSelectedWorld < mAvailableWorlds.Count;

        if (hasSel && ImGui.IsKeyPressed(ImGuiKey.Delete))
            mShowDeleteConfirm = true;

        float btnY = windowSize.Y - 62f;
        float totalW = BUTTON_WIDTH * 4 + BUTTON_SPACING * 3;
        float bx = cx - totalW * 0.5f;

        // Play
        PushDisableableBtn(hasSel, false);
        ImGui.SetCursorPos(new Vector2(bx, btnY));
        if (ImGui.Button("Play", new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)) && hasSel)
        {
            ClickSound();
            Serialization.WorldName = mAvailableWorlds[mSelectedWorld].WorldName;
            var selWorld = mAvailableWorlds[mSelectedWorld];
            OnStartGame?.Invoke(Serialization.GetWorldSize(selWorld.WorldName), mVolSfx, mVolMusic, 0, 0, selWorld.IsCreative);
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
            mIsCreative = false;
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
                ImGui.CloseCurrentPopup();
            PopBtn();

            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }
}
