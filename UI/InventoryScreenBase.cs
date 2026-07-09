// Main class used for any inventory screen. Has stuff mostly related to drawing item stacks and inventory slots. | DA | 3/4/26

using System.Numerics;
using ImGuiNET;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.UI;

/// <summary>
/// Shared base for every ImGui screen that renders inventory-style slot grids (player inventory, chests, furnace, crafting). Provides slot layout constants, slot/item/cursor drawing, click handling (left = pick-up/place/merge, right = split stack), and slot hit-testing for the player's main grid + hotbar. Screens derive from this to reuse the same visuals/interactions rather than reimplementing drag-and-drop slot logic per screen.
/// </summary>
public abstract class InventoryScreenBase
{
    protected const float SLOT_SIZE = 44f * UIHelper.UI_SCALE;
    protected const float ITEM_SIZE = 32f * UIHelper.UI_SCALE;
    protected const float ITEM_PADDING = (SLOT_SIZE - ITEM_SIZE) / 2f;
    protected const float PADDING = 10f * UIHelper.UI_SCALE;
    protected const float SECTION_GAP = 10f * UIHelper.UI_SCALE;
    protected const int COLS = 9;
    protected const int MAIN_ROWS = 3;

    protected static readonly uint ColorBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.85f));
    protected static readonly uint ColorSlot = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 0.60f));
    protected static readonly uint ColorBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 0.80f));
    protected static readonly uint ColorBorderSel = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.90f));
    protected static readonly uint ColorWhite = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
    protected static readonly uint ColorShadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.75f));
    protected static readonly uint ColorTooltipBd = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 1f));

    protected readonly BlockIconRenderer mIconRenderer;
    protected readonly Texture mItemTexture;
    protected ItemStack? mCursorStack;

    protected InventoryScreenBase(BlockIconRenderer iconRenderer, Texture itemTexture)
    {
        mIconRenderer = iconRenderer;
        mItemTexture = itemTexture;
    }

    // Returns the cursor stack to inventory when closing the screen.
    protected void ReturnCursorToInventory()
    {
        if (!mCursorStack.HasValue)
            return;

        Game.Instance.PlayerInventory?.TryAdd(mCursorStack.Value);
        mCursorStack = null;
    }

    // Draws the 9x3 main grid + hotbar row for the player inventory.
    protected void DrawPlayerInventory(ImDrawListPtr drawList, PlayerInventory inv, float contentX, float invY,
        float hotbarY)
    {
        for (int row = 0; row < MAIN_ROWS; row++)
        {
            for (int col = 0; col < COLS; col++)
            {
                DrawInvSlot(drawList, inv, row * COLS + col, contentX + col * SLOT_SIZE, invY + row * SLOT_SIZE, false);
            }
        }

        int selectedHotbar = Game.Instance.Hotbar?.SelectedSlotIndex ?? -1;
        for (int col = 0; col < COLS; col++)
        {
            DrawInvSlot(drawList, inv, PlayerInventory.HOTBAR_START + col, contentX + col * SLOT_SIZE, hotbarY, col == selectedHotbar);
        }
    }

    // Draws the cursor item following the mouse.
    protected void DrawCursorStack(ImDrawListPtr drawList, Vector2 mousePos)
    {
        if (!mCursorStack.HasValue)
            return;

        float cx = mousePos.X - ITEM_SIZE / 2f;
        float cy = mousePos.Y - ITEM_SIZE / 2f;

        DrawItem(drawList, mCursorStack.Value, cx, cy, ITEM_SIZE);

        if (mCursorStack.Value.Count > 1)
        {
            var countStr = mCursorStack.Value.Count.ToString();
            var textSize = ImGui.CalcTextSize(countStr);
            DrawShadowedText(drawList, new Vector2(cx + ITEM_SIZE - textSize.X - 2f, cy + ITEM_SIZE - textSize.Y - 1f), countStr);
        }
    }

    protected void DrawInvSlot(ImDrawListPtr drawList, PlayerInventory inv, int slotIndex, float sx, float sy, bool isSelected)
    {
        var min = new Vector2(sx, sy);
        var max = new Vector2(sx + SLOT_SIZE, sy + SLOT_SIZE);
        drawList.AddRectFilled(min, max, ColorSlot);
        drawList.AddRect(min, max, isSelected ? ColorBorderSel : ColorBorder, 0f, ImDrawFlags.None, isSelected ? 2f : 1f);

        var stack = inv.GetSlot(slotIndex);
        if (!stack.HasValue)
            return;

        DrawItem(drawList, stack.Value, sx + ITEM_PADDING, sy + ITEM_PADDING, ITEM_SIZE);
        DrawCount(drawList, stack.Value.Count, sx, sy);
        UIHelper.DrawDurabilityBar(drawList, stack.Value, sx, sy + SLOT_SIZE, SLOT_SIZE);
    }

    // Draws a generic slot (background + border + item + count).
    protected void DrawSlot(ImDrawListPtr drawList, ItemStack? stack, float sx, float sy, bool isSelected = false)
    {
        var min = new Vector2(sx, sy);
        var max = new Vector2(sx + SLOT_SIZE, sy + SLOT_SIZE);
        drawList.AddRectFilled(min, max, ColorSlot);
        drawList.AddRect(min, max, isSelected ? ColorBorderSel : ColorBorder, 0f, ImDrawFlags.None, isSelected ? 2f : 1f);

        if (!stack.HasValue)
            return;

        DrawItem(drawList, stack.Value, sx + ITEM_PADDING, sy + ITEM_PADDING, ITEM_SIZE);
        DrawCount(drawList, stack.Value.Count, sx, sy);
        UIHelper.DrawDurabilityBar(drawList, stack.Value, sx, sy + SLOT_SIZE, SLOT_SIZE);
    }

    // Draws the stack count in the bottom-right of a slot.
    protected void DrawCount(ImDrawListPtr drawList, int count, float sx, float sy)
    {
        if (count <= 1)
            return;

        var countStr = count.ToString();
        var textSize = ImGui.CalcTextSize(countStr);
        DrawShadowedText(drawList, new Vector2(sx + SLOT_SIZE - textSize.X - 2f, sy + SLOT_SIZE - textSize.Y - 1f),
            countStr);
    }

    protected void DrawItem(ImDrawListPtr drawList, ItemStack stack, float x, float y, float size)
    {
        var min = new Vector2(x, y);
        var max = new Vector2(x + size, y + size);
        if (stack.IsBlock)
        {
            drawList.AddImage(mIconRenderer.GetIcon(stack.Block), min, max, Vector2.Zero, Vector2.One);
        }
        else
        {
            var uv = ItemRegistry.GetItemCoords(stack.Item);
            drawList.AddImage(new IntPtr(mItemTexture.Handle), min, max, new Vector2(uv.TopLeft.X, uv.BottomRight.Y), new Vector2(uv.BottomRight.X, uv.TopLeft.Y));
        }
    }

    // Left-click on a slot: pick up the whole stack onto the cursor, place the cursor down, merge like stacks (up to max stack size), or swap stacks if they differ.
    protected void HandleInvSlotLeft(PlayerInventory inv, int slotIndex)
    {
        var slot = inv.GetSlot(slotIndex);
        if (!mCursorStack.HasValue)
        {
            if (slot.HasValue)
            {
                mCursorStack = slot;
                inv.SetSlot(slotIndex, null);
            }

            return;
        }

        var cursor = mCursorStack.Value;

        if (!slot.HasValue)
        {
            inv.SetSlot(slotIndex, cursor);
            mCursorStack = null;
            return;
        }

        if (slot.Value == cursor)
        {
            // Same item type: top up the slot from the cursor stack, capped at max stack size.
            int space = GetMaxStackSize(cursor) - slot.Value.Count;
            if (space > 0)
            {
                int transfer = Math.Min(space, cursor.Count);
                inv.SetSlot(slotIndex, slot.Value.WithCount(slot.Value.Count + transfer));
                int remaining = cursor.Count - transfer;
                mCursorStack = remaining > 0 ? cursor.WithCount(remaining) : null;

                return;
            }
        }

        // Different item (or same item but full): swap cursor and slot contents.
        inv.SetSlot(slotIndex, cursor);
        mCursorStack = slot;
    }

    // Right-click on a slot: pick up half the stack, place a single item, or add a single item onto a matching cursor stack.
    protected void HandleInvSlotRight(PlayerInventory inv, int slotIndex)
    {
        var slot = inv.GetSlot(slotIndex);
        if (!mCursorStack.HasValue)
        {
            if (!slot.HasValue)
                return;

            // Round up so odd counts favor the cursor half (matches vanilla split behavior).
            int half = (slot.Value.Count + 1) / 2;
            mCursorStack = slot.Value.WithCount(half);
            int remaining = slot.Value.Count - half;
            inv.SetSlot(slotIndex, remaining > 0 ? slot.Value.WithCount(remaining) : null);
            return;
        }

        var cursor = mCursorStack.Value;
        if (!slot.HasValue)
        {
            inv.SetSlot(slotIndex, cursor.WithCount(1));
            int remaining = cursor.Count - 1;
            mCursorStack = remaining > 0 ? cursor.WithCount(remaining) : null;

            return;
        }

        if (slot.Value == cursor && slot.Value.Count < GetMaxStackSize(cursor))
        {
            inv.SetSlot(slotIndex, slot.Value.WithCount(slot.Value.Count + 1));
            int remaining = cursor.Count - 1;
            mCursorStack = remaining > 0 ? cursor.WithCount(remaining) : null;
        }
    }

    protected int GetInvSlotAtMouse(Vector2 mousePos, float slotsX, float mainY, float hotbarY)
    {
        float relX = mousePos.X - slotsX;
        if (relX < 0 || relX >= COLS * SLOT_SIZE)
            return -1;

        int col = (int)(relX / SLOT_SIZE);

        float relMainY = mousePos.Y - mainY;

        if (relMainY >= 0 && relMainY < MAIN_ROWS * SLOT_SIZE)
            return (int)(relMainY / SLOT_SIZE) * COLS + col;

        float relHotY = mousePos.Y - hotbarY;

        if (relHotY >= 0 && relHotY < SLOT_SIZE)
            return PlayerInventory.HOTBAR_START + col;

        return -1;
    }

    protected string GetName(ItemStack stack) => stack.IsBlock ? BlockRegistry.GetName(stack.Block) : ItemRegistry.GetName(stack.Item);

    protected int GetMaxStackSize(ItemStack stack) => stack.IsBlock ? BlockRegistry.Get(stack.Block).MaxStackSize : ItemRegistry.Get(stack.Item).MaxStackSize;

    protected void DrawShadowedText(ImDrawListPtr drawList, Vector2 pos, string text)
    {
        drawList.AddText(pos + Vector2.One, ColorShadow, text);
        drawList.AddText(pos, ColorWhite, text);
    }

    protected void DrawTooltip(ImDrawListPtr drawList, Vector2 mousePos, string text)
    {
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(8f, 6f);
        var bgMin = mousePos + new Vector2(14f, -textSize.Y / 2f - padding.Y);
        var bgMax = bgMin + textSize + padding * 2;
        drawList.AddRectFilled(bgMin, bgMax, ColorBg, 3f);
        drawList.AddRect(bgMin, bgMax, ColorTooltipBd, 3f);
        drawList.AddText(bgMin + padding, ColorWhite, text);
    }
}