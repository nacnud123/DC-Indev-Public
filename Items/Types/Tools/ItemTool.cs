// Abstract base for all tools, provides tier-based durability | DA | 3/8/26
namespace VoxelEngine.Items;

/// <summary>
/// Abstract base for every non-bow tool (pickaxe/sword/axe/shovel/hoe). Provides the shared tier-based durability table; MiningSpeed/AttackDamage/ToolType are further specialized by the per-tool-category abstract classes (ItemPickaxe, ItemSword, etc.), and concrete leaf classes (e.g. ItemIronPickaxe) only need to supply Type/Name/ToolTier/ItemCoords.
/// </summary>
public abstract class ItemTool : Item
{
    public override int MaxStackSize => 1;

    // Durability values follow Minecraft-style tiering: Gold is deliberately the most fragile despite mining fast, Diamond is by far the most durable, and note the values are total use counts, not damage points.
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
