// Furnace screen, input + fuel + output slots with progress bars, plus player inventory | DA | 3/1/26

using System.Numerics;
using ImGuiNET;
using VoxelEngine.BlockEntities;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Net;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.UI;

/// <summary>
/// Furnace block-entity UI: input + fuel slots feeding a smelt recipe (via
/// <see cref="SmeltRegistry"/>) into an output slot, with a flame meter (fuel burn
/// time remaining) and a progress arrow (smelt progress), plus the shared player
/// inventory/hotbar. Actual smelting/burning ticks happen in <see cref="FurnaceData"/>
/// elsewhere; this class only reads that state to draw it and handles slot clicks.
/// </summary>
public class FurnaceScreen : InventoryScreenBase
{
    // Properties (not cached static readonly fields) so these track UIHelper.UI_SCALE live
    // if the player changes UI scaling in the options menu mid-session.
    private static float ARROW_W => 32f * UIHelper.UI_SCALE;
    private static float FLAME_H => 14f * UIHelper.UI_SCALE;
    private static float INNER_GAP => 8f * UIHelper.UI_SCALE;

    // Furnace area: [Input] gap [Arrow] gap [Output]
    //               [Flame]
    //               [Fuel ]
    private static float FURNACE_AREA_W => SLOT_SIZE + INNER_GAP + ARROW_W + INNER_GAP + SLOT_SIZE;
    private static float INV_W => COLS * SLOT_SIZE;
    private static float PANEL_W => INV_W + PADDING * 2;
    private static float FURNACE_BLOCK_H => SLOT_SIZE * 2 + FLAME_H + INNER_GAP * 2;
    private static float INV_BLOCK_H => (MAIN_ROWS + 1) * SLOT_SIZE + SECTION_GAP;
    private static float PANEL_H => FURNACE_BLOCK_H + SECTION_GAP + INV_BLOCK_H + PADDING * 2;

    private static readonly uint ColorFlame = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.5f, 0.1f, 0.85f));
    private static readonly uint ColorArrow = ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.9f, 0.6f, 0.90f));

    private FurnaceData mFurnace = null!;

    protected override WindowKind Kind => WindowKind.Furnace;

    // Composite order: input, fuel, output - see WindowLayout.
    protected override ItemStack? LocalContainerSlot(int index) => index switch
    {
        0 => mFurnace.InputSlot,
        1 => mFurnace.FuelSlot,
        _ => mFurnace.OutputSlot,
    };

    public FurnaceScreen(BlockIconRenderer iconRenderer, Texture itemTexture)
        : base(iconRenderer, itemTexture)
    {
    }

    public void SetFurnace(FurnaceData furnace) => mFurnace = furnace;

    public void OnClose() => CloseScreen();

    public void Render()
    {
        var inv = Game.Instance.PlayerInventory;

        // In multiplayer there is no local FurnaceData - the snapshot and the progress bars are it.
        if (inv == null || (!Networked && mFurnace == null))
            return;

        var io = ImGui.GetIO();
        var drawList = ImGui.GetBackgroundDrawList();
        var displaySize = io.DisplaySize;

        float panelX = (displaySize.X - PANEL_W) / 2f;
        float panelY = (displaySize.Y - PANEL_H) / 2f;
        float contentX = panelX + PADDING;

        float furnaceAreaX = contentX + (INV_W - FURNACE_AREA_W) / 2f;
        float furnaceAreaY = panelY + PADDING;

        float inputX = furnaceAreaX;
        float inputY = furnaceAreaY;
        float fuelX = furnaceAreaX;
        float flameBotY = furnaceAreaY + SLOT_SIZE + INNER_GAP + FLAME_H;
        float fuelY = flameBotY + INNER_GAP;
        float arrowX = furnaceAreaX + SLOT_SIZE + INNER_GAP;
        float arrowY = inputY;
        float outputX = arrowX + ARROW_W + INNER_GAP;
        float outputY = inputY;

        float invY = panelY + PADDING + FURNACE_BLOCK_H + SECTION_GAP;
        float hotbarY = invY + MAIN_ROWS * SLOT_SIZE + SECTION_GAP;

        drawList.AddRectFilled(
            new Vector2(panelX, panelY),
            new Vector2(panelX + PANEL_W, panelY + PANEL_H),
            ColorBg, 6f);

        DrawSlot(drawList, ContainerSlot(0), inputX, inputY);
        DrawSlot(drawList, ContainerSlot(1), fuelX, fuelY);
        DrawOutputSlot(drawList, outputX, outputY);
        DrawFlame(drawList, fuelX, flameBotY);
        DrawArrow(drawList, arrowX, arrowY);

        DrawPlayerInventory(drawList, inv, contentX, invY, hotbarY);

        var mousePos = io.MousePos;

        if (HitsSlot(mousePos, inputX, inputY))
        {
            if (ContainerSlot(0) is { } input)
                DrawTooltip(drawList, mousePos, GetName(input));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !SendContainerClick(0, false))
                HandleFurnaceSlotLeft(ref mFurnace.InputSlot);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !SendContainerClick(0, true))
                HandleFurnaceSlotRight(ref mFurnace.InputSlot);
        }

        if (HitsSlot(mousePos, fuelX, fuelY))
        {
            if (ContainerSlot(1) is { } fuel)
                DrawTooltip(drawList, mousePos, GetName(fuel));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !SendContainerClick(1, false))
                HandleFurnaceSlotLeft(ref mFurnace.FuelSlot);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !SendContainerClick(1, true))
                HandleFurnaceSlotRight(ref mFurnace.FuelSlot);
        }

        if (HitsSlot(mousePos, outputX, outputY))
        {
            if (ContainerSlot(2) is { } output)
                DrawTooltip(drawList, mousePos, GetName(output));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !SendContainerClick(2, false))
                HandleOutputClick();
        }

        int hoveredSlot = GetInvSlotAtMouse(mousePos, contentX, invY, hotbarY);
        if (hoveredSlot >= 0)
        {
            var stack = InventorySlot(inv, hoveredSlot);
            if (stack.HasValue)
                DrawTooltip(drawList, mousePos, GetName(stack.Value));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                ClickInventorySlot(inv, hoveredSlot, false);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                ClickInventorySlot(inv, hoveredSlot, true);
        }

        DrawCursorStack(drawList, mousePos);
    }

    private void DrawOutputSlot(ImDrawListPtr drawList, float sx, float sy)
    {
        var output = ContainerSlot(2);

        var min = new Vector2(sx, sy);
        var max = new Vector2(sx + SLOT_SIZE, sy + SLOT_SIZE);
        drawList.AddRectFilled(min, max, ColorSlot);
        drawList.AddRect(min, max, output.HasValue ? ColorBorderSel : ColorBorder);

        if (output is not { } stack)
            return;

        DrawItem(drawList, stack, sx + ITEM_PADDING, sy + ITEM_PADDING, ITEM_SIZE);
        DrawCount(drawList, stack.Count, sx, sy);
    }

    // Vertical meter, fills bottom-up as fuel burns down (CurrentFuelMax is the burn
    // time of whatever fuel item is currently lit, not a constant).
    private void DrawFlame(ImDrawListPtr drawList, float sx, float flameBotY)
    {
        var windows = Game.Instance.Windows;

        (int remaining, int fuelMax) = Networked
            ? (windows.BurnTime, (int)windows.BurnMax)
            : (mFurnace.BurnTimeRemaining, mFurnace.CurrentFuelMax);

        float flameFrac = fuelMax > 0 ? (float)remaining / fuelMax : 0f;
        float filledH = FLAME_H * flameFrac;

        drawList.AddRectFilled(
            new Vector2(sx, flameBotY - FLAME_H),
            new Vector2(sx + SLOT_SIZE, flameBotY),
            ColorSlot);

        if (filledH > 0)
            drawList.AddRectFilled(
                new Vector2(sx, flameBotY - filledH),
                new Vector2(sx + SLOT_SIZE, flameBotY),
                ColorFlame);

        drawList.AddText(new Vector2(sx + SLOT_SIZE / 2f - 4f, flameBotY - FLAME_H - 1f), ColorWhite, "^");
    }

    // Horizontal meter, fills left-to-right as SmeltProgress advances toward the
    // matched recipe's total tick count. Re-looks-up the recipe each frame since
    // the input slot can change.
    private void DrawArrow(ImDrawListPtr drawList, float arrowX, float arrowY)
    {
        var recipe = SmeltRegistry.FindMatch(ContainerSlot(0));
        int smeltProgress = Networked ? Game.Instance.Windows.CookProgress : mFurnace.SmeltProgress;

        float progress = (recipe != null && recipe.TicksToSmelt > 0)
            ? (float)smeltProgress / recipe.TicksToSmelt
            : 0f;

        float centreY = arrowY + SLOT_SIZE / 2f;
        float filledW = ARROW_W * progress;

        drawList.AddRectFilled(
            new Vector2(arrowX, centreY - 5f),
            new Vector2(arrowX + ARROW_W, centreY + 5f),
            ColorSlot);

        if (filledW > 0)
            drawList.AddRectFilled(
                new Vector2(arrowX, centreY - 5f),
                new Vector2(arrowX + filledW, centreY + 5f),
                ColorArrow);

        drawList.AddText(
            new Vector2(arrowX + 6f, centreY - ImGui.GetTextLineHeight() / 2f), ColorWhite, "=>");
    }

    private void HandleFurnaceSlotLeft(ref ItemStack? slot)
    {
        if (!mCursorStack.HasValue)
        {
            mCursorStack = slot;
            slot = null;
            return;
        }

        var cursor = mCursorStack.Value;
        if (slot.HasValue && slot.Value == cursor)
        {
            int space = GetMaxStackSize(cursor) - slot.Value.Count;
            if (space > 0)
            {
                int transfer = Math.Min(space, cursor.Count);
                slot = slot.Value.WithCount(slot.Value.Count + transfer);
                int remaining = cursor.Count - transfer;
                mCursorStack = remaining > 0 ? cursor.WithCount(remaining) : null;
                return;
            }
        }

        (slot, mCursorStack) = (mCursorStack, slot);
    }

    private void HandleFurnaceSlotRight(ref ItemStack? slot)
    {
        if (!mCursorStack.HasValue)
        {
            if (!slot.HasValue)
                return;

            int half = (slot.Value.Count + 1) / 2;
            mCursorStack = slot.Value.WithCount(half);
            int remaining = slot.Value.Count - half;
            slot = remaining > 0 ? slot.Value.WithCount(remaining) : null;

            return;
        }

        if (!slot.HasValue)
        {
            slot = mCursorStack.Value.WithCount(1);
            int remaining = mCursorStack.Value.Count - 1;
            mCursorStack = remaining > 0 ? mCursorStack.Value.WithCount(remaining) : null;
        }
    }

    private void HandleOutputClick()
    {
        if (!mFurnace.OutputSlot.HasValue)
            return;

        if (!mCursorStack.HasValue)
        {
            mCursorStack = mFurnace.OutputSlot;
            mFurnace.OutputSlot = null;
            return;
        }

        var cursor = mCursorStack.Value;
        if (cursor == mFurnace.OutputSlot.Value)
        {
            int space = GetMaxStackSize(cursor) - cursor.Count;
            int take = Math.Min(space, mFurnace.OutputSlot.Value.Count);

            if (take > 0)
            {
                mCursorStack = cursor.WithCount(cursor.Count + take);
                int remaining = mFurnace.OutputSlot.Value.Count - take;
                mFurnace.OutputSlot = remaining > 0 ? mFurnace.OutputSlot.Value.WithCount(remaining) : null;
            }
        }
    }

    private bool HitsSlot(Vector2 mouse, float sx, float sy) =>
        mouse.X >= sx && mouse.X < sx + SLOT_SIZE &&
        mouse.Y >= sy && mouse.Y < sy + SLOT_SIZE;
}