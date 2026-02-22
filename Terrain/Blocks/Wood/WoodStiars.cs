using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class WoodStiars : Block
{
    public override BlockType Type => BlockType.WoodenStair;
    public override string Name => "Wood Stair";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;
    public override RenderingType RenderType => RenderingType.Stair;

    public override bool IsTransparent => true;
    public override int LightOpacity => 0;
    public override bool IsSolid => true;
    public override bool ShowInInventory => true;
    public override bool IsFlamable => true;

    public override Vector3 BoundsMax => Vector3.One;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}

