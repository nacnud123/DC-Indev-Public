using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockStoneStair : Block
{
    public override BlockType Type => BlockType.StoneStair;
    public override string Name => "Stone Stair";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override RenderingType RenderType => RenderingType.Stair;

    public override float Hardness => 1.5f;
    public override bool IsTransparent => true;
    public override int LightOpacity => 0;
    public override bool IsSolid => true;
    public override bool ShowInInventory => true;

    public override Vector3 BoundsMax => Vector3.One;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
