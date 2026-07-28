using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>The second half of a two-wide chest formed when two single chests are placed adjacent
/// to each other. Not placeable directly (ShowInInventory is false, it's created programmatically
/// when chests merge) and drops a regular Chest item when broken. Its combined inventory is stored
/// separately as a DoubleChestData in BlockEntityManager, keyed by world position.</summary>
public class BlockDoubleChest : Block
{
    public override BlockType Type => BlockType.DoubleChest;
    public override string Name => "Chest";
    public override float Hardness => 2.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;
    public override ToolType PreferredTool => ToolType.Axe;
    public override bool IsFlamable => true;
    override public bool ShowInInventory => false;

    // Breaking either half of a double chest yields a normal single Chest item, not a "double chest" item.
    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromBlock(BlockType.Chest);

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 7);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(0, 7);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(1, 7);
    public override TextureCoords FrontTextureCoords => UvHelper.FromTileCoords(2, 7);
}