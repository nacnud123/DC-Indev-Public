using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Spiderweb block. Non-solid cross-sprite that entities can walk into but which slows
/// their movement (via <see cref="SlowsEntities"/>). Unlike flowers/mushrooms it has no
/// support-block requirement, so it can be placed freely (e.g. in dungeons/mineshafts).
/// </summary>
public class BlockSpiderweb : Block
{
    public override BlockType Type => BlockType.SpiderWeb;
    public override string Name => "Spiderweb";
    public override RenderingType RenderType => RenderingType.Cross;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;
    public override bool IsSolid => false;
    public override int LightOpacity => 0;
    public override float Hardness => 0.2f;
    // Engine reads this to apply a movement-speed penalty to entities occupying the block.
    public override bool SlowsEntities => true;

    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromItem(ItemType.String);

    // Single shared tile - cross-sprite rendering draws only one texture per block.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
