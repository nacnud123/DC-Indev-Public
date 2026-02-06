// Main inventory screen file, holds stuff related to rendering main menu. Also, the inventory order, which is a stupid way to do it, but it works | DA | 2/5/26
using System.Numerics;
using ImGuiNET;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.UI;

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

    private static readonly BlockType[] KInventoryOrder =
    {
        BlockType.Grass, BlockType.Dirt, BlockType.Stone, BlockType.Sand, BlockType.Gravel,
        BlockType.Wood, BlockType.Planks, BlockType.Leaves,
        BlockType.CoalOre, BlockType.IronOre, BlockType.GoldOre, BlockType.DiamondOre,
        BlockType.Bricks, BlockType.Glass, BlockType.Sponge,
        BlockType.Flower, BlockType.GrassTuft, BlockType.BrownMushroom, BlockType.RedMushroom,
        BlockType.Torch, BlockType.Glowstone,
        BlockType.White, BlockType.Black, BlockType.Red, BlockType.Green, BlockType.Blue,
        BlockType.DuncanBlock, BlockType.Bedrock
    };

    private readonly List<Block> mSelectableBlocks;
    private readonly IntPtr mTexturePtr;

    public InventoryScreen(Texture blockAtlasTexture)
    {
        mSelectableBlocks = KInventoryOrder
            .Select(BlockRegistry.Get)
            .Where(b => b.ShowInInventory)
            .ToList();
        mTexturePtr = new IntPtr(blockAtlasTexture.Handle);
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
        var texCoords = block.InventoryTextureCoords;
        var player = Game.Instance.GetPlayer;

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
            mTexturePtr,
            new Vector2(BUTTON_SIZE, BUTTON_SIZE),
            new Vector2(texCoords.TopLeft.X, texCoords.BottomRight.Y),
            new Vector2(texCoords.BottomRight.X, texCoords.TopLeft.Y),
            new Vector4(0, 0, 0, 0),
            new Vector4(1, 1, 1, 1));

        if (clicked)
        {
            player.SelectedBlock = block.Type;
            Game.Instance.CloseInventory();
        }

        ImGui.PopID();
    }
}
