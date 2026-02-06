using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGrass : Block
{
    public override BlockType Type => BlockType.Grass;
    public override string Name => "Grass";
    public override float Hardness => 0.6f;
    
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(1, 1);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(0, 1);
    public override TextureCoords InventoryTextureCoords => SideTextureCoords;
}
