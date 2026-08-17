using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Solid-colored wool-type decoration block (black). Uses the default Hardness/tool
/// properties (breaks by hand); the only distinguishing traits are its texture and flammability.</summary>
public class BlockBlack : Block
{
    public override BlockType Type => BlockType.Black;
    public override string Name => "Black";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
