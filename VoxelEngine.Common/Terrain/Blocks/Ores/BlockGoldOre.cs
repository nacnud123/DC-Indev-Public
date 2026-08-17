using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Ore variant of stone requiring an Iron-tier (or better) pickaxe to yield a drop. Unlike
/// Coal/Diamond ore, this class does not override GetDrop, so the base Block.GetDrop is used:
/// breaking it drops the raw GoldOre block itself (presumably meant to be smelted into an
/// ingot via a furnace, rather than dropping an item directly).
/// </summary>
public class BlockGoldOre : Block
{
    public override BlockType Type => BlockType.GoldOre;
    public override string Name => "Gold Ore";
    public override float Hardness => 2.0f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    // Requires Iron tier (or higher) pickaxe to actually get a drop.
    public override ToolTier MinimumTier => ToolTier.Iron;

    // Atlas tile (1,4); same texture used for all six faces.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(1, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
