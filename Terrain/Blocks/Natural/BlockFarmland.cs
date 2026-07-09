using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockFarmland : Block
{
    public const byte MAX_MOISTURE = 7;

    public override BlockType Type => BlockType.Farmland;
    public override string Name => "Farmland";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Dirt;
    public override ToolType PreferredTool => ToolType.Shovel;
    public override float Hardness => 0.6f;
    public override bool TicksRandomly => true;
    public override bool ShowInInventory => false;
    public override int TickRate => 1;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(8, 1);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(1, 1);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(1, 1);

    // Metadata > 0 means hydrated - use the wet top texture (+1 x in the tile sheet).
    public override TextureCoords GetTopTexture(byte metadata) =>
        metadata > 0 ? UvHelper.FromTileCoords(9, 1) : TopTextureCoords;

    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromBlock(BlockType.Dirt);

    public override void RandomTick(World world, int x, int y, int z, Random random)
    {
        bool hasWater = false;
        for (int dx = -4; dx <= 4 && !hasWater; dx++)
        {
            for (int dz = -4; dz <= 4 && !hasWater; dz++)
            {
                for (int dy = 0; dy <= 1 && !hasWater; dy++)
                {
                    if (world.GetBlock(x + dx, y + dy, z + dz) == BlockType.Water)
                        hasWater = true;
                }
            }
        }

        byte meta = (byte)world.GetMetadata(x, y, z);

        if (hasWater)
        {
            if (meta != MAX_MOISTURE)
            {
                world.SetMetadata(x, y, z, MAX_MOISTURE);
                world.SetChunkAsModified(x, y, z);
            }
        }
        else if (meta > 0)
        {
            world.SetMetadata(x, y, z, (byte)(meta - 1));
            world.SetChunkAsModified(x, y, z);
        }
        else if (!HasCropAbove(world, x, y, z))
        {
            world.SetBlock(x, y, z, BlockType.Dirt);
            world.SetChunkAsModified(x, y, z);
        }
    }

    public override void ScheduledTick(World world, int x, int y, int z, Random random)
    {
        if (BlockRegistry.IsSolid(world.GetBlock(x, y + 1, z)))
        {
            world.SetBlock(x, y, z, BlockType.Dirt);
            world.SetChunkAsModified(x, y, z);
        }
    }

    public override void OnEntityWalking(World world, int x, int y, int z, Random random)
    {
        if (random.Next(4) == 0)
        {
            world.SetBlock(x, y, z, BlockType.Dirt);
            world.SetChunkAsModified(x, y, z);
        }
    }

    private bool HasCropAbove(World world, int x, int y, int z)
    {
        var above = world.GetBlock(x, y + 1, z);

        if (above == BlockType.Air) 
            return false;

        var def = BlockRegistry.Get(above);
        return def.NeedsSupportBelow && def.BlocksThatCanSupport.Contains(BlockType.Farmland);
    }
}
