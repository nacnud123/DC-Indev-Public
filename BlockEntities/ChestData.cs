// Main class for the chest block entity. Holds reference to the chest's inventory. | DA | 3/5/26


using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.BlockEntities;

/// <summary>
/// Block entity backing a single chest block: a fixed 27-slot inventory keyed to a world position by <see cref="BlockEntityManager"/>. Handles inserting items (stacking onto existing piles first, then filling empty slots) and dumping its contents as dropped-item entities when the chest is broken.
/// </summary>
public class ChestData : IBlockEntity
{
    public const int CHEST_SLOTS = 27;

    public Vector3i Position { get; set; }
    // Nullable per-slot storage; null means the slot is empty.
    private readonly ItemStack?[] mSlots = new ItemStack?[CHEST_SLOTS];

    public ChestData(Vector3i position)
    {
        Position = position;
    }

    /// <summary>Gets the item stack (or null if empty) in the given slot index.</summary>
    public ItemStack? GetSlot(int index) => mSlots[index];

    /// <summary>Directly overwrites a slot's contents, used by UI drag/drop and save loading.</summary>
    public void SetSlot(int index, ItemStack? stack) => mSlots[index] = stack;

    /// <summary>
    /// Attempts to insert as much of <paramref name="stack"/> as will fit: first tops off any existing slots holding the same item type up to their max stack size, then fills empty slots with the remainder. Returns true if at least one item was placed (not necessarily all of it).
    /// </summary>
    public bool TryAdd(ItemStack stack)
    {
        int remaining = stack.Count;

        // Pass 1: merge into existing partial stacks of the same item/block type.
        for (int i = 0; i < CHEST_SLOTS && remaining > 0; i++)
        {
            if (mSlots[i] == null)
                continue;

            var slot = mSlots[i]!.Value;

            if (slot != stack)
                continue;

            int max = GetMaxStackSize(stack);

            if (slot.Count >= max)
                continue;

            int transfer = Math.Min(max - slot.Count, remaining);
            mSlots[i] = slot.WithCount(slot.Count + transfer);
            remaining -= transfer;
        }

        // Pass 2: place any leftover into empty slots.
        for (int i = 0; i < CHEST_SLOTS && remaining > 0; i++)
        {
            if (mSlots[i] != null)
                continue;

            int place = Math.Min(GetMaxStackSize(stack), remaining);
            mSlots[i] = stack.WithCount(place);
            remaining -= place;
        }

        return remaining < stack.Count;
    }

    /// <summary>Spawns every non-empty slot as a dropped item entity centered on the chest block, then (implicitly) leaves the chest emptied by the caller removing it.</summary>
    public void DropContents(World world)
    {
        var center = new Vector3(Position.X + 0.5f, Position.Y + 0.5f, Position.Z + 0.5f);
        foreach (var item in mSlots)
        {
            if (item.HasValue)
                world.AddEntity(new DroppedItemEntity(center, item.Value, Game.Instance.WorldTexture));
        }
    }

    /// <summary>Looks up the max stack size for a stack's underlying block or item type from the appropriate registry.</summary>
    private int GetMaxStackSize(ItemStack stack)
    {
        if (stack.IsBlock)
            return BlockRegistry.Get(stack.Block).MaxStackSize;

        return ItemRegistry.Get(stack.Item).MaxStackSize;
    }
}