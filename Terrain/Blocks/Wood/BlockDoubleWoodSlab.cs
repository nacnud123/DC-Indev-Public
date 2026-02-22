using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockDoubleWoodSlab : Block
{
    public override BlockType Type => BlockType.DoubleWoodSlab;
    public override string Name => "Double Wood Slab";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;
    public override bool IsFlamable => true;
    public override bool ShowInInventory => false;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(2, 2);
}
