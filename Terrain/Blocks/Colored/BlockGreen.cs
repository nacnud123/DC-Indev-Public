using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGreen : Block
{
    public override BlockType Type => BlockType.Green;
    public override string Name => "Green";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
