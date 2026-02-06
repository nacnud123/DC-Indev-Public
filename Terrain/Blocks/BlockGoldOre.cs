using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGoldOre : Block
{
    public override BlockType Type => BlockType.GoldOre;
    public override string Name => "Gold Ore";

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(1, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
