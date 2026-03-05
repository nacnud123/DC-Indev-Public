using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockCoalOre : Block
{
    public override BlockType Type => BlockType.CoalOre;
    public override string Name => "Coal Ore";
    public override float Hardness => 1.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;
    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromItem(ItemType.Coal);

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(1, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
