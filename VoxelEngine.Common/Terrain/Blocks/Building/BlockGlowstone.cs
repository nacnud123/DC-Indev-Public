using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Naturally-occurring light source block; provides maximum block light for illuminating
/// builds and caves.</summary>
public class BlockGlowstone : Block
{
    public override BlockType Type => BlockType.Glowstone;
    public override string Name => "Glowstone";
    public override int LightEmission => 15; // Maximum brightness.
    public override float Hardness => 0.3f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Glass;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(4, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
