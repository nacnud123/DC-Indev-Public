// Abstract pickaxe — subclasses provide Type, Name, ToolTier, ItemCoords | DA | 3/8/26
namespace VoxelEngine.Items;

public abstract class ItemPickaxe : ItemTool
{
    public override ToolType ToolType => ToolType.Pickaxe;

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
        ToolTier.Wood    => 2,
        ToolTier.Stone   => 3,
        ToolTier.Iron    => 4,
        ToolTier.Gold    => 2,
        ToolTier.Diamond => 5,
        _                => 1
    };
}
