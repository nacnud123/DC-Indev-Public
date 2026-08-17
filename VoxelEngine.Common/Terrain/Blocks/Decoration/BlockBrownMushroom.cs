using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Decorative, non-solid mushroom block. Renders as a cross-sprite (two intersecting
/// quads) rather than a full cube, and can only exist on top of a supporting block.
/// Purely decorative - carries no tick logic of its own beyond the base support check.
/// </summary>
public class BlockBrownMushroom : Block
{
    public override BlockType Type => BlockType.BrownMushroom;
    public override string Name => "Brown Mushroom";
    public override RenderingType RenderType => RenderingType.Cross;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Grass;
    public override bool IsFlamable => true;
    public override bool IsSolid => false;
    public override bool IsReplaceable => true;
    public override float Hardness => 0.0f;
    public override int LightOpacity => 0;
    public override bool SuffocatesBeneath => true;
    public override bool NeedsSupportBelow => true;
    // Base Block logic breaks this block automatically if the block beneath it is not
    // one of these types (checked by the engine, not here) - see NeedsSupportBelow.
    public override List<BlockType> BlocksThatCanSupport => new List<BlockType>() { BlockType.Grass, BlockType.Dirt, BlockType.Stone };

    // Same tile used for all six faces - cross-sprite rendering only ever draws one texture.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(4, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
