using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGravel : Block
{
    public override BlockType Type => BlockType.Gravel;
    public override string Name => "Gravel";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Gravel;
    public override ToolType PreferredTool => ToolType.Shovel;

    public override bool GravityBlock => true;
    public override float Hardness => 0.5f;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    public override ItemStack? GetDrop(byte metadata) => Game.Instance.GameRandom.NextSingle() < 0.1f ? ItemStack.FromItem(ItemType.Flint) : ItemStack.FromBlock(BlockType.Gravel);
}
