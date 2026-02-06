using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockPlanks : Block
{
    public override BlockType Type => BlockType.Planks;
    public override string Name => "Planks";

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
