// Abstract base for all armor pieces | DA | 3/8/26
namespace VoxelEngine.Items;

/// <summary>
/// Abstract base for every equippable armor piece. Subclasses (one per tier/slot combo, e.g. ItemIronHelmet) provide Type, Name, ItemCoords, ArmorSlot, ArmorTier, and ArmorPoints. Armor never stacks since only one of a kind is worn at a time.
/// </summary>
public abstract class ItemArmor : Item
{
    public override int MaxStackSize => 1;
}
