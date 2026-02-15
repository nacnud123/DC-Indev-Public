using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockRed : Block
{
    public override BlockType Type => BlockType.Red;
    public override string Name => "Red";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
