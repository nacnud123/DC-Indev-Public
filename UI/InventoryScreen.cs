// Main class for the inventory screen it has the survival inventory screen, armor column + 9×3 main grid + 9×1 hotbar row + 2×2 craft grid | DA | 2/26/26

using System.Numerics;
using ImGuiNET;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.UI;

/// <summary>
/// The survival-mode inventory screen (opened with the Inventory keybind): armor column on the left, the shared 9x3 main grid + hotbar in the middle, and a 2x2 crafting grid with a result slot on the right. Crafting output is recomputed by <see cref="CraftingGrid"/> whenever its input slots change.
/// </summary>
public class InventoryScreen : InventoryScreenBase
{
    // Craft area
    private const int CRAFT_COLS = 2;
    private const int CRAFT_ROWS = 2;
    private const float CRAFT_GAP = 8f * UIHelper.UI_SCALE;
    private const float ARROW_W = 24f * UIHelper.UI_SCALE;
    private const float CRAFT_AREA_W = CRAFT_COLS * SLOT_SIZE + CRAFT_GAP + ARROW_W + CRAFT_GAP + SLOT_SIZE;

    // Armor column
    private const int ARMOR_COUNT = 4;
    private const float ARMOR_GAP = 8f * UIHelper.UI_SCALE;
    private const float ARMOR_COL_W = SLOT_SIZE + ARMOR_GAP;
    private const float ARMOR_Y_OFFSET = 25f * UIHelper.UI_SCALE;

    private const float PANEL_W = ARMOR_COL_W + COLS * SLOT_SIZE + CRAFT_GAP + CRAFT_AREA_W + PADDING * 2;
    private const float PANEL_H = (MAIN_ROWS + 1) * SLOT_SIZE + SECTION_GAP + PADDING * 2;

    // Empty-slot placeholder icons
    private static readonly TextureCoords[] ArmorSlotIcons =
    [
        UvHelper.FromTileCoords(15, 3),
        UvHelper.FromTileCoords(15, 2),
        UvHelper.FromTileCoords(15, 1),
        UvHelper.FromTileCoords(15, 0),
    ];

    private CraftingGrid mCraftGrid = new CraftingGrid(CRAFT_COLS, CRAFT_ROWS);

    public InventoryScreen(BlockIconRenderer iconRenderer, Texture itemTexture)
        : base(iconRenderer, itemTexture)
    {
    }

    // Dump any items left in the crafting grid and cursor back into the player's inventory so closing the screen never destroys items.
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

        // All positions below are derived from PANEL_W/PANEL_H so the whole layout stays centered and self-consistent if slot size or panel size changes.
        float panelX = (displaySize.X - PANEL_W) / 2f;
        float panelY = (displaySize.Y - PANEL_H) / 2f;
        float armorX = panelX + PADDING;
        float slotsX = armorX + ARMOR_COL_W;
        float mainY = panelY + PADDING;
        float hotbarY = mainY + MAIN_ROWS * SLOT_SIZE + SECTION_GAP;

        float armorColH = ARMOR_COUNT * SLOT_SIZE;
        float armorColY = mainY + (MAIN_ROWS * SLOT_SIZE - armorColH) / 2f + ARMOR_Y_OFFSET;

        float craftX = slotsX + COLS * SLOT_SIZE + CRAFT_GAP;
        float craftY = mainY + (MAIN_ROWS * SLOT_SIZE - CRAFT_ROWS * SLOT_SIZE) / 2f;
        float arrowX = craftX + CRAFT_COLS * SLOT_SIZE + CRAFT_GAP;
        float arrowY = craftY + (CRAFT_ROWS * SLOT_SIZE - SLOT_SIZE) / 2f;
        float resultX = arrowX + ARROW_W + CRAFT_GAP;
        float resultY = arrowY;

        drawList.AddRectFilled(new Vector2(panelX, panelY), new Vector2(panelX + PANEL_W, panelY + PANEL_H), ColorBg, 6f);

        for (int i = 0; i < ARMOR_COUNT; i++)
            DrawArmorSlot(drawList, inv, (ArmorSlot)i, armorX, armorColY + i * SLOT_SIZE);

        DrawPlayerInventory(drawList, inv, slotsX, mainY, hotbarY);

        for (int row = 0; row < CRAFT_ROWS; row++)
        {
            for (int col = 0; col < CRAFT_COLS; col++)
            {
                DrawSlot(drawList, mCraftGrid.GetSlot(row * CRAFT_COLS + col), craftX + col * SLOT_SIZE, craftY + row * SLOT_SIZE);
            }
        }

        drawList.AddText(new Vector2(arrowX, arrowY + (SLOT_SIZE - ImGui.GetTextLineHeight()) / 2f), ColorWhite, "=>");
        DrawSlot(drawList, mCraftGrid.Result, resultX, resultY, isSelected: mCraftGrid.Result.HasValue);

        var mousePos = io.MousePos;

        int hoveredArmor = GetArmorSlotAtMouse(mousePos, armorX, armorColY);
        if (hoveredArmor >= 0)
        {
            var stack = inv.GetArmorSlot((ArmorSlot)hoveredArmor);

            if (stack.HasValue)
                DrawTooltip(drawList, mousePos, ItemRegistry.GetName(stack.Value.Item));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                HandleArmorLeftClick(inv, (ArmorSlot)hoveredArmor);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                HandleArmorRightClick(inv, (ArmorSlot)hoveredArmor);
        }

        int hoveredSlot = GetInvSlotAtMouse(mousePos, slotsX, mainY, hotbarY);

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

        bool mouseOverResult = mousePos.X >= resultX && mousePos.X < resultX + SLOT_SIZE && mousePos.Y >= resultY && mousePos.Y < resultY + SLOT_SIZE;

        if (mouseOverResult && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            HandleResultClick();

        DrawCursorStack(drawList, mousePos);
    }

    private void DrawArmorSlot(ImDrawListPtr drawList, PlayerInventory inv, ArmorSlot slot, float sx, float sy)
    {
        var min = new Vector2(sx, sy);
        var max = new Vector2(sx + SLOT_SIZE, sy + SLOT_SIZE);
        drawList.AddRectFilled(min, max, ColorSlot);
        drawList.AddRect(min, max, ColorBorder);

        var stack = inv.GetArmorSlot(slot);
        if (stack.HasValue)
        {
            DrawItem(drawList, stack.Value, sx + ITEM_PADDING, sy + ITEM_PADDING, ITEM_SIZE);

            UIHelper.DrawDurabilityBar(drawList, stack.Value, sx, sy + SLOT_SIZE, SLOT_SIZE);
        }
        else
        {
            // Placeholder icon
            var uv = ArmorSlotIcons[(int)slot];
            var iconMin = new Vector2(sx + ITEM_PADDING, sy + ITEM_PADDING);
            var iconMax = new Vector2(iconMin.X + ITEM_SIZE, iconMin.Y + ITEM_SIZE);
            drawList.AddImage(new IntPtr(mItemTexture.Handle), iconMin, iconMax, new Vector2(uv.TopLeft.X, uv.BottomRight.Y), new Vector2(uv.BottomRight.X, uv.TopLeft.Y), ColorBorder);
        }
    }

    private int GetArmorSlotAtMouse(Vector2 mousePos, float armorX, float armorColY)
    {
        float relX = mousePos.X - armorX;
        float relY = mousePos.Y - armorColY;

        if (relX < 0 || relX >= SLOT_SIZE)
            return -1;

        if (relY < 0 || relY >= ARMOR_COUNT * SLOT_SIZE)
            return -1;

        return (int)(relY / SLOT_SIZE);
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

    // Equip logic: an item may only be placed in an armor slot if it's flagged as armor for that specific slot (e.g. a helmet can't go in the boots slot). Only 1 armor piece is ever equipped at once, so extras beyond count 1 spill back into the main inventory.
    private void HandleArmorLeftClick(PlayerInventory inv, ArmorSlot slot)
    {
        var existing = inv.GetArmorSlot(slot);
        if (!mCursorStack.HasValue)
        {
            if (existing.HasValue)
            {
                mCursorStack = existing;
                inv.SetArmorSlot(slot, null);
            }

            return;
        }

        var cursor = mCursorStack.Value;
        if (cursor.IsBlock)
            return;

        var def = ItemRegistry.Get(cursor.Item);
        if (!def.IsArmor || def.ArmorSlot != slot)
            return;

        inv.SetArmorSlot(slot, cursor.WithCount(1));
        mCursorStack = existing.HasValue ? existing : null;
        if (cursor.Count > 1)
            inv.TryAdd(cursor.WithCount(cursor.Count - 1));
    }

    private void HandleArmorRightClick(PlayerInventory inv, ArmorSlot slot)
    {
        if (!mCursorStack.HasValue)
        {
            var existing = inv.GetArmorSlot(slot);
            if (existing.HasValue)
            {
                mCursorStack = existing;
                inv.SetArmorSlot(slot, null);
            }

            return;
        }

        var cursor = mCursorStack.Value;
        if (cursor.IsBlock)
            return;

        var def = ItemRegistry.Get(cursor.Item);
        if (!def.IsArmor || def.ArmorSlot != slot)
            return;

        if (inv.GetArmorSlot(slot) == null)
        {
            inv.SetArmorSlot(slot, cursor.WithCount(1));
            int remaining = cursor.Count - 1;
            mCursorStack = remaining > 0 ? cursor.WithCount(remaining) : null;
        }
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

    // Taking the crafted result: stack onto the cursor if it matches, otherwise (cursor empty or holding something else) either place it on the cursor or push it straight into the main inventory.
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

/* =============================================================
   CREATIVE MODE INVENTORY kept for future use
   =============================================================

// Main inventory screen file, holds stuff related to rendering main menu. Also, the inventory order, which is a stupid way to do it, but it works | DA | 2/5/26
public class InventoryScreen
{
    private const int BUTTONS_PER_ROW = 6;
    private const float BUTTON_SIZE = 64.0f;
    private static readonly Vector2 KWindowPadding = new(50, 80);
    private static readonly Vector2 KContentPadding = new(20, 30);

    private const ImGuiWindowFlags K_WINDOW_FLAGS =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoBringToFrontOnFocus |
        ImGuiWindowFlags.NoFocusOnAppearing;

    private readonly List<Block> mSelectableBlocks;
    private readonly BlockIconRenderer mIconRenderer;

    public InventoryScreen(BlockIconRenderer iconRenderer)
    {
        mSelectableBlocks = BlockRegistry.GetAll()
            .Where(b => b.ShowInInventory)
            .ToList();
        mIconRenderer = iconRenderer;
    }

    public void Render()
    {
        var displaySize = ImGui.GetIO().DisplaySize;
        var windowPos = KWindowPadding;
        var windowSize = displaySize - KWindowPadding * 2;

        ImGui.SetNextWindowPos(windowPos);
        ImGui.SetNextWindowSize(windowSize);
        ImGui.Begin("Block Selection Menu", K_WINDOW_FLAGS);

        ImGui.Dummy(new Vector2(0, KContentPadding.Y));

        float contentWidth = ImGui.GetContentRegionAvail().X - KContentPadding.X * 2;
        float itemSpacing = ImGui.GetStyle().ItemSpacing.X;
        int numRows = (int)Math.Ceiling((double)mSelectableBlocks.Count / BUTTONS_PER_ROW);

        for (int row = 0; row < numRows; row++)
        {
            int startIndex = row * BUTTONS_PER_ROW;
            int endIndex = Math.Min(startIndex + BUTTONS_PER_ROW, mSelectableBlocks.Count);
            int buttonsInRow = endIndex - startIndex;

            float rowWidth = buttonsInRow * BUTTON_SIZE + (buttonsInRow - 1) * itemSpacing;
            float centerOffset = Math.Max(0, (contentWidth - rowWidth) * 0.5f);

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + KContentPadding.X + centerOffset);

            for (int i = startIndex; i < endIndex; i++)
            {
                if (i > startIndex)
                    ImGui.SameLine();

                var block = mSelectableBlocks[i];
                RenderBlockButton(i, block);
            }
        }

        ImGui.Dummy(new Vector2(0, KContentPadding.Y));
        ImGui.End();
    }

    private void RenderBlockButton(int index, Block block)
    {
        var player = Game.Instance.GetPlayer;
        var iconPtr = mIconRenderer.GetIcon(block.Type);

        ImGui.PushID(index);

        if (player.SelectedBlock == block.Type)
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos() + new Vector2(3, 2);
            uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
            drawList.AddRect(
                pos - Vector2.One * 2,
                pos + new Vector2(BUTTON_SIZE + 4, BUTTON_SIZE + 4),
                borderColor, 0, 0, 3.0f);
        }

        bool clicked = ImGui.ImageButton(
            $"block_{block.Type}",
            iconPtr,
            new Vector2(BUTTON_SIZE, BUTTON_SIZE),
            new Vector2(0, 0),
            new Vector2(1, 1),
            new Vector4(0, 0, 0, 0),
            new Vector4(1, 1, 1, 1));

        if (ImGui.IsItemHovered())
        {
            var mousePos = ImGui.GetMousePos();
            var textSize = ImGui.CalcTextSize(block.Name);
            var popupSize = textSize + new Vector2(16, 16);

            ImGui.SetNextWindowPos(mousePos + new Vector2(12, -popupSize.Y / 2f));
            ImGui.SetNextWindowSize(popupSize);
            ImGui.SetNextWindowBgAlpha(0.85f);

            ImGui.Begin("##block_tooltip",
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav |
                ImGuiWindowFlags.NoInputs);

            ImGui.SetCursorPos((popupSize - textSize) * 0.5f);
            ImGui.Text(block.Name);

            ImGui.End();
        }

        if (clicked)
        {
            player.SelectedBlock = block.Type;
            Game.Instance.Hotbar?.SetBlockInCurrentSlot(block.Type);
            Game.Instance.CloseInventory();
        }

        ImGui.PopID();
    }

    public void DropCurrentBlock()
    {
    }
}
============================================================= */