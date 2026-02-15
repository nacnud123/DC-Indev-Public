using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockBlue : Block
{
    public override BlockType Type => BlockType.Blue;
    public override string Name => "Blue";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
