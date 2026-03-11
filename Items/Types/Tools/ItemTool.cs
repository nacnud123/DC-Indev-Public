// Abstract base for all tools, provides tier-based durability | DA | 3/8/26
namespace VoxelEngine.Items;

public abstract class ItemTool : Item
{
    public override int MaxStackSize => 1;

    public override int MaxDurability => ToolTier switch
    {
        ToolTier.Wood    => 60,
        ToolTier.Stone   => 132,
        ToolTier.Iron    => 251,
        ToolTier.Gold    => 33,
        ToolTier.Diamond => 1562,
        _                => -1
    };
}
