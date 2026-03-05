using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockFarmland : Block
{
    public override BlockType Type => BlockType.Farmland;
    public override string Name => "Farmland";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Dirt;
    public override ToolType PreferredTool => ToolType.Shovel;
    public override float Hardness => 0.6f;

    public override TextureCoords TopTextureCoords    => UvHelper.FromTileCoords(8, 1);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(1, 1);
    public override TextureCoords SideTextureCoords   => UvHelper.FromTileCoords(1, 1);

    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromBlock(BlockType.Dirt);
}
