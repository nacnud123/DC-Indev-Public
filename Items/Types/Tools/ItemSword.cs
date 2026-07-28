// Abstract sword - subclasses provide Type, Name, ToolTier, ItemCoords | DA | 3/8/26
namespace VoxelEngine.Items;

/// <summary>Abstract base for all sword tiers — highest attack damage of any tool category, no mining-speed bonus; subclasses supply Type, Name, ToolTier, ItemCoords.</summary>
public abstract class ItemSword : ItemTool
{
    public override ToolType ToolType => ToolType.Sword;
    public override float MiningSpeed => 1f;

    public override int AttackDamage => ToolTier switch
    {
        ToolTier.Wood    => 4,
        ToolTier.Stone   => 5,
        ToolTier.Iron    => 6,
        ToolTier.Gold    => 4,
        ToolTier.Diamond => 7,
        _                => 1
    };
}
