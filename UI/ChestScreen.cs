// Main chest screen class. Shows and renders the chest inventory, also handles what to do when clicking | DA | 3/4/26

using System.Numerics;
using ImGuiNET;
using VoxelEngine.BlockEntities;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;

namespace VoxelEngine.UI;

public class ChestScreen : InventoryScreenBase
{
    private const int CHEST_ROWS = 3;
    private const float INV_W = COLS * SLOT_SIZE;
    private const float PANEL_W = INV_W + PADDING * 2;
    private const float CHEST_BLOCK_H = CHEST_ROWS * SLOT_SIZE;
    private const float INV_BLOCK_H = (MAIN_ROWS + 1) * SLOT_SIZE + SECTION_GAP;
    private const float PANEL_H = CHEST_BLOCK_H + SECTION_GAP + INV_BLOCK_H + PADDING * 2;

    private ChestData mChest = null!;

    public ChestScreen(BlockIconRenderer iconRenderer, Texture itemTexture)
        : base(iconRenderer, itemTexture)
    {
    }

    public void SetChest(ChestData chest) => mChest = chest;

    public void OnClose()
    {
        if (!mCursorStack.HasValue)
            return;

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
        float chestY = panelY + PADDING;
        float invY = chestY + CHEST_BLOCK_H + SECTION_GAP;
        float hotbarY = invY + MAIN_ROWS * SLOT_SIZE + SECTION_GAP;

        drawList.AddRectFilled(
            new Vector2(panelX, panelY),
            new Vector2(panelX + PANEL_W, panelY + PANEL_H),
            ColorBg, 6f);

        for (int row = 0; row < CHEST_ROWS; row++)
        {
            for (int col = 0; col < COLS; col++)
            {
                DrawSlot(drawList, mChest.GetSlot(row * COLS + col), contentX + col * SLOT_SIZE,
                    chestY + row * SLOT_SIZE);
            }
        }

        DrawPlayerInventory(drawList, inv, contentX, invY, hotbarY);

        var mousePos = io.MousePos;

        int hoveredChest = GetGridSlotAtMouse(mousePos, contentX, chestY, CHEST_ROWS);
        if (hoveredChest >= 0)
        {
            var stack = mChest.GetSlot(hoveredChest);
            if (stack.HasValue)
                DrawTooltip(drawList, mousePos, GetName(stack.Value));
            
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) 
                HandleChestLeft(hoveredChest);
            
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) 
                HandleChestRight(hoveredChest);
        }

        int hoveredInv = GetInvSlotAtMouse(mousePos, contentX, invY, hotbarY);
        if (hoveredInv >= 0)
        {
            var stack = inv.GetSlot(hoveredInv);
            if (stack.HasValue)
                DrawTooltip(drawList, mousePos, GetName(stack.Value));
            
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) 
                HandleInvSlotLeft(inv, hoveredInv);
            
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) 
                HandleInvSlotRight(inv, hoveredInv);
        }

        DrawCursorStack(drawList, mousePos);
    }

    private int GetGridSlotAtMouse(Vector2 mousePos, float slotsX, float slotsY, int rows)
    {
        float relX = mousePos.X - slotsX;
        float relY = mousePos.Y - slotsY;
        if (relX < 0 || relX >= COLS * SLOT_SIZE || relY < 0 || relY >= rows * SLOT_SIZE)
            return -1;
        
        return (int)(relY / SLOT_SIZE) * COLS + (int)(relX / SLOT_SIZE);
    }

    private void HandleChestLeft(int index)
    {
        var slot = mChest.GetSlot(index);
        var cursor = mCursorStack;

        if (!cursor.HasValue)
        {
            mCursorStack = slot;
            mChest.SetSlot(index, null);
            return;
        }

        if (slot.HasValue && slot.Value == cursor.Value)
        {
            int space = GetMaxStackSize(cursor.Value) - slot.Value.Count;
            if (space > 0)
            {
                int transfer = Math.Min(space, cursor.Value.Count);
                mChest.SetSlot(index, slot.Value.WithCount(slot.Value.Count + transfer));
                int remaining = cursor.Value.Count - transfer;
                mCursorStack = remaining > 0 ? cursor.Value.WithCount(remaining) : null;
                return;
            }
        }

        mChest.SetSlot(index, cursor);
        mCursorStack = slot;
    }

    private void HandleChestRight(int index)
    {
        var slot = mChest.GetSlot(index);
        var cursor = mCursorStack;

        if (!cursor.HasValue)
        {
            if (!slot.HasValue) 
                return;
            
            int half = (slot.Value.Count + 1) / 2;
            mCursorStack = slot.Value.WithCount(half);
            int remaining = slot.Value.Count - half;
            mChest.SetSlot(index, remaining > 0 ? slot.Value.WithCount(remaining) : null);
            
            return;
        }

        if (!slot.HasValue)
        {
            mChest.SetSlot(index, cursor.Value.WithCount(1));
            int remaining = cursor.Value.Count - 1;
            mCursorStack = remaining > 0 ? cursor.Value.WithCount(remaining) : null;
            return;
        }

        if (slot.Value == cursor.Value && slot.Value.Count < GetMaxStackSize(cursor.Value))
        {
            mChest.SetSlot(index, slot.Value.WithCount(slot.Value.Count + 1));
            int remaining = cursor.Value.Count - 1;
            mCursorStack = remaining > 0 ? cursor.Value.WithCount(remaining) : null;
        }
    }
}