// Abstract axe — subclasses provide Type, Name, ToolTier, ItemCoords | DA | 3/8/26
namespace VoxelEngine.Items;

public abstract class ItemAxe : ItemTool
{
    public override ToolType ToolType => ToolType.Axe;

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
        ToolTier.Wood    => 3,
        ToolTier.Stone   => 4,
        ToolTier.Iron    => 5,
        ToolTier.Gold    => 3,
        ToolTier.Diamond => 6,
        _                => 1
    };
}
