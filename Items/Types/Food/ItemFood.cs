// Abstract base for all edible food items | DA | 3/8/26
namespace VoxelEngine.Items;

public abstract class ItemFood : Item
{
    public override bool IsFood => true;
    public override int MaxStackSize => 64;
}
