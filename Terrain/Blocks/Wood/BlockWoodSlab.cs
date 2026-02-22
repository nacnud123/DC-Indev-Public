using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockWoodSlab : Block
{
    public override BlockType Type => BlockType.WoodSlab;
    public override string Name => "Wood Slab";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;
    public override RenderingType RenderType => RenderingType.Slab;
    public override bool IsSolid => true;
    public override bool IsTransparent => true;
    public override int LightOpacity => 0;
    public override Vector3 BoundsMax => new Vector3(1, 0.5f, 1);
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromPartialTile(2, 2, 0, 0, 16, 8);
    public override TextureCoords InventoryTextureCoords => SideTextureCoords;
}
