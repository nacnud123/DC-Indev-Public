using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Solid-colored wool-type decoration block (green). Uses the default Hardness/tool
/// properties (breaks by hand); the only distinguishing traits are its texture and flammability.</summary>
public class BlockGreen : Block
{
    public override BlockType Type => BlockType.Green;
    public override string Name => "Green";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
