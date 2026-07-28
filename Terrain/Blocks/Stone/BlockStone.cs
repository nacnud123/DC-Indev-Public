using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// The base/most common terrain stone block. Notably, mining natural Stone does not drop
/// Stone itself - it drops Cobblestone (GetDrop override below), mirroring classic
/// Minecraft-style "stone must be smelted to get stone back" behavior.
/// </summary>
public class BlockStone : Block
{
    public override BlockType Type => BlockType.Stone;
    public override string Name => "Stone";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;

    public override float Hardness => 1.5f;
    // Overrides the default "drop self" behavior: mined Stone yields Cobblestone, not Stone.
    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromBlock(BlockType.CobbleStone);

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
