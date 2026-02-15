using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockCobblestone : Block
{
    public override BlockType Type => BlockType.CobbleStone;
    public override string Name => "Cobblestone";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;

    public override float Hardness => 1.5f;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
