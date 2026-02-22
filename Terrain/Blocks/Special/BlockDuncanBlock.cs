using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockDuncanBlock : Block
{
    public override BlockType Type => BlockType.DuncanBlock;
    public override string Name => "Duncan Block";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Glass;
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
