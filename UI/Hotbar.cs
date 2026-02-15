// Makes the UI for the hotbar. Builds the hotbar from ImGui elements and not textures, same with the hotbar selector. Also, has functions for moving the hotbar selector and setting a block in a slot | DA | 2/14/26
using System.Numerics;
using ImGuiNET;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.UI;

internal class Hotbar
{
    private const int HOTBAR_SLOTS = 10;
    private const float UI_SCALE = 1.5f;

    private const float SLOT_SIZE = 32f * UI_SCALE;
    private const float ITEM_SIZE = 24f * UI_SCALE;
    private const float ITEM_PADDING = (SLOT_SIZE - ITEM_SIZE) / 2f;
    private const float BAR_PADDING = 4f * UI_SCALE;
    private const float HOTBAR_WIDTH = HOTBAR_SLOTS * SLOT_SIZE + BAR_PADDING * 2;
    private const float HOTBAR_HEIGHT = SLOT_SIZE + BAR_PADDING * 2;

    private int mSelectedSlot;
    private readonly BlockType?[] mSlots = new BlockType?[HOTBAR_SLOTS];
    private readonly IntPtr mAtlasTexturePtr;

    public Hotbar(Texture blockAtlasTexture)
    {
        mAtlasTexturePtr = new IntPtr(blockAtlasTexture.Handle);
    }

    public void SetHotbarSlot(int slot)
    {
        mSelectedSlot = Math.Clamp(slot, 0, HOTBAR_SLOTS - 1);
    }

    public void ScrollSlot(int direction)
    {
        mSelectedSlot += direction;
        if (mSelectedSlot < 0) 
            mSelectedSlot = HOTBAR_SLOTS - 1;
        else if (mSelectedSlot >= HOTBAR_SLOTS) 
            mSelectedSlot = 0;
    }

    public void SetBlockInSlot(int slot, BlockType block)
    {
        if (slot >= 0 && slot < HOTBAR_SLOTS)
            mSlots[slot] = block;
    }

    public void SetBlockInCurrentSlot(BlockType block)
    {
        mSlots[mSelectedSlot] = block;
    }

    public BlockType? GetSelectedBlock()
    {
        return mSlots[mSelectedSlot];
    }

    public void Render()
    {
        var displaySize = ImGui.GetIO().DisplaySize;
        var drawList = ImGui.GetBackgroundDrawList();

        float hotbarX = (displaySize.X - HOTBAR_WIDTH) / 2f;
        float hotbarY = displaySize.Y - HOTBAR_HEIGHT - 10f;

        uint bgColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.75f));
        uint slotColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 0.6f));
        uint slotBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 0.8f));
        uint selectedBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f));

        // Background
        drawList.AddRectFilled(
            new Vector2(hotbarX, hotbarY),
            new Vector2(hotbarX + HOTBAR_WIDTH, hotbarY + HOTBAR_HEIGHT),
            bgColor, 4f);

        // Slots
        for (int i = 0; i < HOTBAR_SLOTS; i++)
        {
            float slotX = hotbarX + BAR_PADDING + i * SLOT_SIZE;
            float slotY = hotbarY + BAR_PADDING;

            var min = new Vector2(slotX, slotY);
            var max = new Vector2(slotX + SLOT_SIZE, slotY + SLOT_SIZE);

            drawList.AddRectFilled(min, max, slotColor);

            if (i == mSelectedSlot)
                drawList.AddRect(min, max, selectedBorder, 0f, ImDrawFlags.None, 3f);
            else
                drawList.AddRect(min, max, slotBorder);

            // Block icon
            var block = mSlots[i];
            if (block.HasValue)
            {
                var texCoords = BlockRegistry.GetParticleTexture(block.Value);
                float itemX = slotX + ITEM_PADDING;
                float itemY = slotY + ITEM_PADDING;

                drawList.AddImage(
                    mAtlasTexturePtr,
                    new Vector2(itemX, itemY),
                    new Vector2(itemX + ITEM_SIZE, itemY + ITEM_SIZE),
                    new Vector2(texCoords.TopLeft.X, texCoords.BottomRight.Y),
                    new Vector2(texCoords.BottomRight.X, texCoords.TopLeft.Y));
            }
        }
    }
}
