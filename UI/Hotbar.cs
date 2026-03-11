// Main class for the Hotbar it renders the 9 selected inventory slots and the active item name. Reads slot contents from PlayerInventory. | DA | 2/14/26

using System.Numerics;
using ImGuiNET;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.UI;

internal class Hotbar
{
    private const int HOTBAR_SLOTS = PlayerInventory.HOTBAR_SLOTS;
    private const int HOTBAR_START = PlayerInventory.HOTBAR_START;
    private const float SLOT_SIZE = 48f * UIHelper.UI_SCALE;
    private const float ITEM_SIZE = 36f * UIHelper.UI_SCALE;
    private const float ITEM_PADDING = (SLOT_SIZE - ITEM_SIZE) / 2f;
    private const float BAR_PADDING = 6f * UIHelper.UI_SCALE;
    private const float HOTBAR_WIDTH = HOTBAR_SLOTS * SLOT_SIZE + BAR_PADDING * 2;
    private const float HOTBAR_HEIGHT = SLOT_SIZE + BAR_PADDING * 2;

    private static readonly uint ColorBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.75f));
    private static readonly uint ColorSlot = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 0.60f));
    private static readonly uint ColorBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 0.80f));
    private static readonly uint ColorBorderSel = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.90f));
    private static readonly uint ColorWhite = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
    private static readonly uint ColorShadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.75f));

    private int mSelectedSlot;
    private readonly PlayerInventory mInventory;
    private readonly BlockIconRenderer mIconRenderer;
    private readonly Texture mItemTexture;

    public int SelectedSlotIndex => mSelectedSlot;

    public float GetHotbarX(float displayWidth) => (displayWidth - HOTBAR_WIDTH) / 2f;
    public float GetHotbarY(float displayHeight) => displayHeight - HOTBAR_HEIGHT - 10f;
    public float HotbarWidth => HOTBAR_WIDTH;

    public Hotbar(BlockIconRenderer iconRenderer, Texture itemTexture, PlayerInventory inventory)
    {
        mIconRenderer = iconRenderer;
        mItemTexture = itemTexture;
        mInventory = inventory;
    }

    private ItemStack? GetSlot(int i) => mInventory.GetSlot(HOTBAR_START + i);
    private void SetSlot(int i, ItemStack? s) => mInventory.SetSlot(HOTBAR_START + i, s);

    public void SetHotbarSlot(int slot) => mSelectedSlot = Math.Clamp(slot, 0, HOTBAR_SLOTS - 1);

    public void ScrollSlot(int direction)
    {
        mSelectedSlot = (mSelectedSlot + direction + HOTBAR_SLOTS) % HOTBAR_SLOTS;
    }

    public void SetBlockInSlot(int slot, BlockType block)
    {
        if (slot >= 0 && slot < HOTBAR_SLOTS)
            SetSlot(slot, ItemStack.FromBlock(block));
    }

    public void SetItemInSlot(int slot, ItemType item)
    {
        if (slot >= 0 && slot < HOTBAR_SLOTS)
            SetSlot(slot, ItemStack.FromItem(item));
    }

    public void SetBlockInCurrentSlot(BlockType block) => SetSlot(mSelectedSlot, ItemStack.FromBlock(block));
    public void ClearCurrentSlot() => SetSlot(mSelectedSlot, null);

    public BlockType? GetSelectedBlock()
    {
        var stack = GetSlot(mSelectedSlot);
        return stack.HasValue && stack.Value.IsBlock ? stack.Value.Block : null;
    }

    public ItemStack? GetSelectedStack() => GetSlot(mSelectedSlot);

    public void Render()
    {
        var displaySize = ImGui.GetIO().DisplaySize;
        var drawList = ImGui.GetBackgroundDrawList();

        float hotbarX = (displaySize.X - HOTBAR_WIDTH) / 2f;
        float hotbarY = displaySize.Y - HOTBAR_HEIGHT - 10f;

        drawList.AddRectFilled(
            new Vector2(hotbarX, hotbarY),
            new Vector2(hotbarX + HOTBAR_WIDTH, hotbarY + HOTBAR_HEIGHT),
            ColorBg, 4f);

        for (int i = 0; i < HOTBAR_SLOTS; i++)
        {
            float slotX = hotbarX + BAR_PADDING + i * SLOT_SIZE;
            float slotY = hotbarY + BAR_PADDING;
            var min = new Vector2(slotX, slotY);
            var max = new Vector2(slotX + SLOT_SIZE, slotY + SLOT_SIZE);

            drawList.AddRectFilled(min, max, ColorSlot);
            drawList.AddRect(min, max, i == mSelectedSlot ? ColorBorderSel : ColorBorder, 0f, ImDrawFlags.None, i == mSelectedSlot ? 3f : 1f);

            var stack = GetSlot(i);
            if (!stack.HasValue) 
                continue;


            DrawItem(drawList, stack.Value, slotX + ITEM_PADDING, slotY + ITEM_PADDING, ITEM_SIZE);

            if (stack.Value.Count > 1)
            {
                var countStr = stack.Value.Count.ToString();
                var textSize = ImGui.CalcTextSize(countStr);
                var countPos = new Vector2(slotX + SLOT_SIZE - textSize.X - 2f, slotY + SLOT_SIZE - textSize.Y - 1f);
                DrawShadowedText(drawList, countPos, countStr);
            }

            UIHelper.DrawDurabilityBar(drawList, stack.Value, slotX, slotY + SLOT_SIZE, SLOT_SIZE);
        }

        var selected = GetSlot(mSelectedSlot);
        if (selected.HasValue)
        {
            string name = selected.Value.IsBlock ? BlockRegistry.GetName(selected.Value.Block) : ItemRegistry.GetName(selected.Value.Item);
            var textSize = ImGui.CalcTextSize(name);
            var labelPos = new Vector2((displaySize.X - textSize.X) / 2f, hotbarY - textSize.Y - 8f);
            DrawShadowedText(drawList, labelPos, name);
        }
    }

    private void DrawItem(ImDrawListPtr drawList, ItemStack stack, float x, float y, float size)
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

    private static void DrawShadowedText(ImDrawListPtr drawList, Vector2 pos, string text)
    {
        drawList.AddText(pos + Vector2.One, ColorShadow, text);
        drawList.AddText(pos, ColorWhite, text);
    }
}