// Main class for the creative mode inventory. Two-tab block/item picker using ImGui image buttons. | DA | 3/8/26

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.UI;

/// <summary>
/// Creative-mode item picker: a tabbed grid of every block/item flagged as selectable, clicking one puts it directly in the player's hand/hotbar (no crafting or inventory slots involved - creative mode has unlimited items). Uses ImGui image buttons rather than the custom slot-drawing used by <see cref="InventoryScreenBase"/>.
/// </summary>
public class CreativeInventoryScreen
{
    private const int BUTTONS_PER_ROW = 9;
    private const float BUTTON_SIZE = 64.0f;
    private readonly Vector2 KWindowPadding = new(50, 80);
    private readonly Vector2 KContentPadding = new(20, 30);

    private const ImGuiWindowFlags K_WINDOW_FLAGS =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoBringToFrontOnFocus |
        ImGuiWindowFlags.NoFocusOnAppearing;

    private readonly List<Block> mSelectableBlocks;
    private readonly List<(ItemType Type, Item Item)> mSelectableItems;
    private readonly BlockIconRenderer mIconRenderer;
    private readonly Texture mItemTexture;

    public CreativeInventoryScreen(BlockIconRenderer iconRenderer, Texture itemTexture)
    {
        mIconRenderer = iconRenderer;
        mItemTexture = itemTexture;

        mSelectableBlocks = BlockRegistry.GetAll().Where(b => b.ShowInInventory).ToList();

        mSelectableItems = ItemRegistry.GetAll().ToList();
    }

    public void OnClose() { }

    public void Render()
    {
        var displaySize = ImGui.GetIO().DisplaySize;

        ImGui.SetNextWindowPos(KWindowPadding);
        ImGui.SetNextWindowSize(displaySize - KWindowPadding * 2);
        ImGui.Begin("Creative Inventory", K_WINDOW_FLAGS);

        if (ImGui.BeginTabBar("##creative_tabs"))
        {
            if (ImGui.BeginTabItem("Blocks"))
            {
                RenderButtonGrid(mSelectableBlocks.Count, i => RenderBlockButton(i, mSelectableBlocks[i]));
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Items"))
            {
                RenderButtonGrid(mSelectableItems.Count, i => RenderItemButton(i, mSelectableItems[i].Type, mSelectableItems[i].Item));
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    // Renders a centered grid of image buttons, calling renderButton(index) for each slot. Row width is computed per-row (not just per-grid) so the last, possibly-partial row is still centered on its own rather than left-aligned under a full grid.
    private void RenderButtonGrid(int count, Action<int> renderButton)
    {
        ImGui.Dummy(new Vector2(0, KContentPadding.Y));

        float contentWidth = ImGui.GetContentRegionAvail().X - KContentPadding.X * 2;
        float itemSpacing = ImGui.GetStyle().ItemSpacing.X;
        int numRows = (int)Math.Ceiling((double)count / BUTTONS_PER_ROW);

        for (int row = 0; row < numRows; row++)
        {
            int start = row * BUTTONS_PER_ROW;
            int end = Math.Min(start + BUTTONS_PER_ROW, count);
            int inRow = end - start;

            float rowWidth = inRow * BUTTON_SIZE + (inRow - 1) * itemSpacing;
            float centerOffset = Math.Max(0, (contentWidth - rowWidth) * 0.5f);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + KContentPadding.X + centerOffset);

            for (int i = start; i < end; i++)
            {
                if (i > start) 
                    ImGui.SameLine();

                renderButton(i);
            }
        }

        ImGui.Dummy(new Vector2(0, KContentPadding.Y));
    }

    private void RenderBlockButton(int index, Block block)
    {
        var player = Game.Instance.GetPlayer;
        var iconPtr = mIconRenderer.GetIcon(block.Type);

        ImGui.PushID($"b_{index}");

        if (player.SelectedBlock == block.Type)
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos() + new Vector2(3, 2);
            uint yellow = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 1f));
            drawList.AddRect(pos - Vector2.One * 2, pos + new Vector2(BUTTON_SIZE + 4, BUTTON_SIZE + 4), yellow, 0, 0, 3f);
        }

        bool clicked = ImGui.ImageButton($"block_{block.Type}", iconPtr, new Vector2(BUTTON_SIZE, BUTTON_SIZE), Vector2.Zero, Vector2.One, new Vector4(0, 0, 0, 0), new Vector4(1, 1, 1, 1));

        RenderTooltip(block.Name);

        if (clicked)
        {
            player.SelectedBlock = block.Type;
            Game.Instance.Hotbar?.SetBlockInCurrentSlot(block.Type);
            Game.Instance.CloseInventory();
        }

        ImGui.PopID();
    }

    private void RenderItemButton(int index, ItemType type, Item item)
    {
        var uv = item.ItemCoords;
        // Flip V: atlas coords are top-left origin but ImGui image UVs expect bottom-left-origin sampling here, so swap TopLeft.Y/BottomRight.Y.
        var uv0 = new Vector2(uv.TopLeft.X, uv.BottomRight.Y);
        var uv1 = new Vector2(uv.BottomRight.X, uv.TopLeft.Y);

        ImGui.PushID($"i_{index}");

        bool clicked = ImGui.ImageButton($"item_{type}", new IntPtr(mItemTexture.Handle), new Vector2(BUTTON_SIZE, BUTTON_SIZE), uv0, uv1, new Vector4(0, 0, 0, 0), new Vector4(1, 1, 1, 1));

        RenderTooltip(item.Name);

        if (clicked)
        {
            var hotbar = Game.Instance.Hotbar;

            if (hotbar != null)
                hotbar.SetItemInSlot(hotbar.SelectedSlotIndex, type);

            Game.Instance.CloseInventory();
        }

        ImGui.PopID();
    }

    private void RenderTooltip(string name)
    {
        if (!ImGui.IsItemHovered()) 
            return;

        var mousePos = ImGui.GetMousePos();
        var textSize = ImGui.CalcTextSize(name);
        var popupSize = textSize + new Vector2(16, 16);

        ImGui.SetNextWindowPos(mousePos + new Vector2(12, -popupSize.Y / 2f));
        ImGui.SetNextWindowSize(popupSize);
        ImGui.SetNextWindowBgAlpha(0.85f);

        ImGui.Begin("##creative_tooltip",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs);

        ImGui.SetCursorPos((popupSize - textSize) * 0.5f);
        ImGui.Text(name);

        ImGui.End();
    }
}
