using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Single storage chest. This class only defines the block's physical properties/textures
/// (distinct front face for the latch); the actual inventory contents are stored separately, keyed
/// by world position, in BlockEntityManager as a ChestData - opening/closing UI logic lives elsewhere
/// (Player interaction / UI screens), not here.</summary>
public class BlockChest : Block
{
    public override BlockType Type => BlockType.Chest;
    public override string Name => "Chest";
    public override float Hardness => 2.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;
    public override ToolType PreferredTool => ToolType.Axe;
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 7);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(0, 7);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(1, 7);
    public override TextureCoords FrontTextureCoords => UvHelper.FromTileCoords(2, 7);
}
