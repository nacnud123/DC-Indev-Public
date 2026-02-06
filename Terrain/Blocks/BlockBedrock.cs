using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockBedrock: Block
{
    public override BlockType Type => BlockType.Bedrock;
    public override string Name => "Bedrock";
    public override float Hardness => 1f;
    public override bool IsBreakable => false;
    public override bool ShowInInventory => false;
    
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}