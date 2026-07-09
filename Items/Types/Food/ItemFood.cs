// Abstract base for all edible food items | DA | 3/8/26
namespace VoxelEngine.Items;

/// <summary>
/// Abstract base for every edible item. Marks the item as food (enabling the "eat" interaction) and gives it a large default stack size like other consumables. Subclasses only need to override Type/Name/ItemCoords/FoodRestore.
/// </summary>
public abstract class ItemFood : Item
{
    public override bool IsFood => true;
    public override int MaxStackSize => 64;
}
