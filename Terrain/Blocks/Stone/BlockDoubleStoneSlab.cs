using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockDoubleStoneSlab : Block
{
    public override BlockType Type => BlockType.DoubleStoneslab;
    public override string Name => "Double Stone Slab";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override bool ShowInInventory => false;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(4, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
