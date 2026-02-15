using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockDiamondOre : Block
{
    public override BlockType Type => BlockType.DiamondOre;
    public override string Name => "Diamond Ore";
    public override float Hardness => 2.0f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
