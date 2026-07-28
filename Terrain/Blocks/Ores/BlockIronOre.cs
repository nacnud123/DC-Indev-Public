using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Ore variant of stone requiring a Stone-tier (or better) pickaxe to yield a drop. Like
/// GoldOre, this does not override GetDrop, so breaking it drops the raw IronOre block itself
/// via the base Block.GetDrop (intended to be smelted into an ingot in a furnace).
/// </summary>
public class BlockIronOre : Block
{
    public override BlockType Type => BlockType.IronOre;
    public override string Name => "Iron Ore";
    public override float Hardness => 1.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    // Requires Stone tier (or higher) pickaxe to actually get a drop.
    public override ToolTier MinimumTier => ToolTier.Stone;

    // Atlas tile (2,3); same texture used for all six faces.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
