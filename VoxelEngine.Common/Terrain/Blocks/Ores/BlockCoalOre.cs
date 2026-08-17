using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Ore variant of stone that yields raw Coal items when mined. Stone-tier hardness/break
/// material; any tool at or above Wood tier (i.e. any pickaxe) is sufficient to get a drop,
/// so coal is intentionally available from the very first tool the player crafts.
/// </summary>
public class BlockCoalOre : Block
{
    public override BlockType Type => BlockType.CoalOre;
    public override string Name => "Coal Ore";
    public override float Hardness => 1.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    // Lowest possible tier gate - coal ore drops for any pickaxe, including wood.
    public override ToolTier MinimumTier => ToolTier.Wood;
    // Overrides the default "drop self as block" behavior: breaking this block yields
    // a Coal item, not a CoalOre block.
    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromItem(ItemType.Coal);

    // Atlas tile (1,3); same texture used for all six faces.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(1, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
