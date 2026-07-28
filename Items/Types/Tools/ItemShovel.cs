// Abstract shovel - subclasses provide Type, Name, ToolTier, ItemCoords | DA | 3/8/26
namespace VoxelEngine.Items;

/// <summary>Abstract base for all shovel tiers — fast at dirt/sand/gravel-type blocks, weakest attack damage; subclasses supply Type, Name, ToolTier, ItemCoords.</summary>
public abstract class ItemShovel : ItemTool
{
    public override ToolType ToolType => ToolType.Shovel;

    public override float MiningSpeed => ToolTier switch
    {
        ToolTier.Wood    => 2f,
        ToolTier.Stone   => 4f,
        ToolTier.Iron    => 6f,
        ToolTier.Gold    => 12f,
        ToolTier.Diamond => 8f,
        _                => 1f
    };

    public override int AttackDamage => ToolTier switch
    {
        ToolTier.Wood    => 1,
        ToolTier.Stone   => 2,
        ToolTier.Iron    => 3,
        ToolTier.Gold    => 1,
        ToolTier.Diamond => 4,
        _                => 1
    };
}
