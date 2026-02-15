using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockMossyCobblestone: Block
{
    public override BlockType Type => BlockType.MossyCobblestone;
    public override string Name => "Mossy Cobblestone";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;

    public override float Hardness => 1.5f;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}


