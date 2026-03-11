// Abstract hoe — all hoe tiers share the same OnUse (till farmland) | DA | 3/8/26
using OpenTK.Mathematics;
using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

public abstract class ItemHoe : ItemTool
{
    public override ToolType ToolType => ToolType.Hoe;
    public override float MiningSpeed => 1f;
    public override int AttackDamage => 1;

    public override bool OnUse(World world, Vector3i blockPos, Vector3i? placePos)
    {
        var b = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);
        if (b != BlockType.Grass && b != BlockType.Dirt)
            return false;

        if (world.GetBlock(blockPos.X, blockPos.Y + 1, blockPos.Z) != BlockType.Air)
            return false;

        world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Farmland);
        world.SetChunkAsModified(blockPos.X, blockPos.Y, blockPos.Z);
        return true;
    }
}
