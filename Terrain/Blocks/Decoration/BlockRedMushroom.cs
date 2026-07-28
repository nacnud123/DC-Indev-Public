using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Decorative red mushroom block. Non-solid cross-sprite; can grow on grass, dirt, or
/// stone (unlike flowers, which are grass/dirt only) since real mushrooms grow in caves.
/// Carries no tick logic beyond the base support check.
/// </summary>
public class BlockRedMushroom : Block
{
    public override BlockType Type => BlockType.RedMushroom;
    public override string Name => "Red Mushroom";
    public override RenderingType RenderType => RenderingType.Cross;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Dirt;
    public override bool IsFlamable => true;
    public override bool IsSolid => false;
    public override bool IsReplaceable => true;
    public override float Hardness => 0.0f;
    public override int LightOpacity => 0;
    public override bool SuffocatesBeneath => true;
    public override bool NeedsSupportBelow => true;
    public override List<BlockType> BlocksThatCanSupport => new List<BlockType>() { BlockType.Grass, BlockType.Dirt, BlockType.Stone };

    // Single shared tile - cross-sprite rendering draws only one texture per block.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
