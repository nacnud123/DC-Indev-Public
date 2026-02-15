using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockIronOre : Block
{
    public override BlockType Type => BlockType.IronOre;
    public override string Name => "Iron Ore";
    public override float Hardness => 1.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
