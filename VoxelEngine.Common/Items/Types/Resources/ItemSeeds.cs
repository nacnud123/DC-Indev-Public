
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemSeeds : Item
{
    public override ItemType Type => ItemType.Seeds;
    public override string Name => "Seeds";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(8, 7);
    public override int MaxStackSize => 64;

    public override bool OnUse(World world, Vector3i blockPos, Vector3i? placePos)
    {
        if (world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z) != BlockType.Farmland)
            return false;

        int px = blockPos.X, py = blockPos.Y + 1, pz = blockPos.Z;
        if (world.GetBlock(px, py, pz) != BlockType.Air)
            return false;

        world.SetBlock(px, py, pz, BlockType.WheatStage0);
        world.SetChunkAsModified(px, py, pz);
        return true;
    }
}
