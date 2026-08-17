using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Solid-colored wool-type decoration block (red). Uses the default Hardness/tool
/// properties (breaks by hand); the only distinguishing traits are its texture and flammability.</summary>
public class BlockRed : Block
{
    public override BlockType Type => BlockType.Red;
    public override string Name => "Red";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
