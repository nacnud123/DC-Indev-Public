// Abstract hoe — all hoe tiers share the same OnUse (till farmland) | DA | 3/8/26

using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

/// <summary>
/// Abstract base for all hoe tiers. Unlike other tools, hoes don't scale mining speed or attack damage by tier (all tiers behave identically); their only real function is the shared till-farmland OnUse below, so concrete subclasses just supply Type/Name/ToolTier/ItemCoords.
/// </summary>
public abstract class ItemHoe : ItemTool
{
    public override ToolType ToolType => ToolType.Hoe;
    public override float MiningSpeed => 1f;
    public override int AttackDamage => 1;

    /// <summary>Tills grass/dirt into farmland, but only if the block above is open air (nothing already sitting on top).</summary>
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
