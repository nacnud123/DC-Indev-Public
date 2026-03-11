// Workbench crafting screen, 3x3 craft grid + player inventory | DA | 2/26/26

using System.Numerics;
using ImGuiNET;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.UI;

public class CraftingScreen : InventoryScreenBase
{
    private const int CRAFT_COLS = 3;
    private const int CRAFT_ROWS = 3;
    private const float CRAFT_GAP = 8f * UIHelper.UI_SCALE;
    private const float ARROW_W = 24f * UIHelper.UI_SCALE;
    private const float CRAFT_AREA_W = CRAFT_COLS * SLOT_SIZE + CRAFT_GAP + ARROW_W + CRAFT_GAP + SLOT_SIZE;

    private const float INV_W = COLS * SLOT_SIZE;
    private const float PANEL_W = INV_W + PADDING * 2;
    private const float CRAFT_BLOCK_H = CRAFT_ROWS * SLOT_SIZE;
    private const float INV_BLOCK_H = (MAIN_ROWS + 1) * SLOT_SIZE + SECTION_GAP;
    private const float PANEL_H = CRAFT_BLOCK_H + SECTION_GAP + INV_BLOCK_H + PADDING * 2;

    private CraftingGrid mCraftGrid = new CraftingGrid(CRAFT_COLS, CRAFT_ROWS);

    public CraftingScreen(BlockIconRenderer iconRenderer, Texture itemTexture)
        : base(iconRenderer, itemTexture)
    {
    }

    public void OnClose()
    {
        var inv = Game.Instance.PlayerInventory;

        if (inv != null)
            mCraftGrid.ReturnItemsTo(inv);

        ReturnCursorToInventory();
    }

    public void Render()
    {
        var inv = Game.Instance.PlayerInventory;

        if (inv == null)
            return;

        var io = ImGui.GetIO();
        var drawList = ImGui.GetBackgroundDrawList();
        var displaySize = io.DisplaySize;

        float panelX = (displaySize.X - PANEL_W) / 2f;
        float panelY = (displaySize.Y - PANEL_H) / 2f;
        float contentX = panelX + PADDING;

        float craftAreaOffsetX = (INV_W - CRAFT_AREA_W) / 2f;
        float craftX = contentX + craftAreaOffsetX;
        float craftY = panelY + PADDING;
        float arrowX = craftX + CRAFT_COLS * SLOT_SIZE + CRAFT_GAP;
        float arrowY = craftY + (CRAFT_ROWS * SLOT_SIZE - SLOT_SIZE) / 2f;
        float resultX = arrowX + ARROW_W + CRAFT_GAP;
        float resultY = arrowY;
        float invY = craftY + CRAFT_BLOCK_H + SECTION_GAP;
        float hotbarY = invY + MAIN_ROWS * SLOT_SIZE + SECTION_GAP;

        drawList.AddRectFilled(new Vector2(panelX, panelY), new Vector2(panelX + PANEL_W, panelY + PANEL_H), ColorBg,
            6f);

        for (int row = 0; row < CRAFT_ROWS; row++)
        {
            for (int col = 0; col < CRAFT_COLS; col++)
            {
                DrawCraftSlot(drawList, row * CRAFT_COLS + col, craftX + col * SLOT_SIZE, craftY + row * SLOT_SIZE);
            }
        }

        drawList.AddText(new Vector2(arrowX, arrowY + (SLOT_SIZE - ImGui.GetTextLineHeight()) / 2f), ColorWhite, "=>");
        DrawResultSlot(drawList, resultX, resultY);

        DrawPlayerInventory(drawList, inv, contentX, invY, hotbarY);

        var mousePos = io.MousePos;

        int hoveredSlot = GetInvSlotAtMouse(mousePos, contentX, invY, hotbarY);
        if (hoveredSlot >= 0)
        {
            var stack = inv.GetSlot(hoveredSlot);
            if (stack.HasValue)
                DrawTooltip(drawList, mousePos, GetName(stack.Value));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                HandleInvSlotLeft(inv, hoveredSlot);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                HandleInvSlotRight(inv, hoveredSlot);
        }

        int hoveredCraft = GetCraftSlotAtMouse(mousePos, craftX, craftY);
        if (hoveredCraft >= 0)
        {
            var stack = mCraftGrid.GetSlot(hoveredCraft);
            if (stack.HasValue)
                DrawTooltip(drawList, mousePos, GetName(stack.Value));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                HandleCraftLeftClick(hoveredCraft);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                HandleCraftRightClick(hoveredCraft);
        }

        bool mouseOverResult = mousePos.X >= resultX && mousePos.X < resultX + SLOT_SIZE &&
                               mousePos.Y >= resultY && mousePos.Y < resultY + SLOT_SIZE;

        if (mouseOverResult && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            HandleResultClick();

        DrawCursorStack(drawList, mousePos);
    }

    private void DrawCraftSlot(ImDrawListPtr drawList, int index, float sx, float sy)
    {
        var stack = mCraftGrid.GetSlot(index);
        DrawSlot(drawList, stack, sx, sy);
    }

    private void DrawResultSlot(ImDrawListPtr drawList, float sx, float sy)
    {
        bool hasResult = mCraftGrid.Result.HasValue;
        DrawSlot(drawList, mCraftGrid.Result, sx, sy, isSelected: hasResult);
    }

    private int GetCraftSlotAtMouse(Vector2 mousePos, float craftX, float craftY)
    {
        float relX = mousePos.X - craftX;
        float relY = mousePos.Y - craftY;
        if (relX < 0 || relX >= CRAFT_COLS * SLOT_SIZE)
            return -1;

        if (relY < 0 || relY >= CRAFT_ROWS * SLOT_SIZE)
            return -1;

        return (int)(relY / SLOT_SIZE) * CRAFT_COLS + (int)(relX / SLOT_SIZE);
    }

    private void HandleCraftLeftClick(int index)
    {
        var slot = mCraftGrid.GetSlot(index);
        if (!mCursorStack.HasValue)
        {
            if (slot.HasValue)
            {
                mCursorStack = slot;
                mCraftGrid.SetSlot(index, null);
            }

            return;
        }

        var cursor = mCursorStack.Value;
        if (!slot.HasValue)
        {
            mCraftGrid.SetSlot(index, cursor);
            mCursorStack = null;
            return;
        }

        if (slot.Value == cursor)
        {
            int space = GetMaxStackSize(cursor) - slot.Value.Count;
            if (space > 0)
            {
                int transfer = Math.Min(space, cursor.Count);
                mCraftGrid.SetSlot(index, slot.Value.WithCount(slot.Value.Count + transfer));
                int remaining = cursor.Count - transfer;
                mCursorStack = remaining > 0 ? cursor.WithCount(remaining) : null;
                return;
            }
        }

        mCraftGrid.SetSlot(index, cursor);
        mCursorStack = slot;
    }

    private void HandleCraftRightClick(int index)
    {
        var slot = mCraftGrid.GetSlot(index);
        if (!mCursorStack.HasValue)
        {
            if (slot.HasValue)
            {
                int half = (slot.Value.Count + 1) / 2;
                mCursorStack = slot.Value.WithCount(half);
                int remaining = slot.Value.Count - half;
                mCraftGrid.SetSlot(index, remaining > 0 ? slot.Value.WithCount(remaining) : null);
            }

            return;
        }

        var cursor = mCursorStack.Value;
        if (!slot.HasValue)
        {
            mCraftGrid.SetSlot(index, cursor.WithCount(1));
            int remaining = cursor.Count - 1;
            mCursorStack = remaining > 0 ? cursor.WithCount(remaining) : null;
            return;
        }

        if (slot.Value == cursor && slot.Value.Count < GetMaxStackSize(cursor))
        {
            mCraftGrid.SetSlot(index, slot.Value.WithCount(slot.Value.Count + 1));
            int remaining = cursor.Count - 1;
            mCursorStack = remaining > 0 ? cursor.WithCount(remaining) : null;
        }
    }

    private void HandleResultClick()
    {
        var result = mCraftGrid.TakeResult();

        if (result == null)
            return;

        if (!mCursorStack.HasValue)
        {
            mCursorStack = result;
            return;
        }

        var cursor = mCursorStack.Value;
        if (cursor == result.Value)
        {
            int space = GetMaxStackSize(cursor) - cursor.Count;
            
            if (space >= result.Value.Count)
                mCursorStack = cursor.WithCount(cursor.Count + result.Value.Count);
        }
        else
        {
            Game.Instance.PlayerInventory?.TryAdd(result.Value);
        }
    }
}