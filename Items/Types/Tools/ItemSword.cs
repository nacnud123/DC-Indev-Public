// Abstract sword — subclasses provide Type, Name, ToolTier, ItemCoords | DA | 3/8/26
namespace VoxelEngine.Items;

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
