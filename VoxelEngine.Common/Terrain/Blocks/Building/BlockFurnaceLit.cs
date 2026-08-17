using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>The actively-smelting variant of BlockFurnace, swapped in while fuel is burning. Not
/// player-placeable directly (ShowInInventory false) and drops a regular Furnace item when broken;
/// its lit texture and light emission are purely visual, the shared FurnaceData (inventory/progress)
/// lives in BlockEntityManager under the same world position regardless of which variant is placed.</summary>
public class BlockFurnaceLit : Block
{
    public override BlockType Type => BlockType.FurnaceLit;
    public override string Name => "Furnace";
    public override float Hardness => 3.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;
    public override bool ShowInInventory => false;
    public override int LightEmission => 13; // Glowing firebox light, close to full brightness (15).

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(7, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(7, 2);
    public override TextureCoords FrontTextureCoords => UvHelper.FromTileCoords(3, 6);

    // Always drops the unlit Furnace item, never a "lit furnace" item.
    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromBlock(BlockType.Furnace);
}
