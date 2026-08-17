// What a click on an inventory slot means. Shared, so both ends agree. | Stage 10

using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Net;

public enum ClickType : byte { LeftClick, RightClick, ShiftLeftClick, ShiftRightClick }

/// <summary>
/// The fiddliest "obvious" code in the project - every container interaction is one of these cases,
/// and getting any one wrong duplicates or deletes items. Singleplayer runs through it too, so a bug
/// shows up in both modes rather than only the one you test less.
/// </summary>
public static class InventoryClick
{
    /// <summary>
    /// Applies a click. <paramref name="cursor"/> is the stack floating on the mouse; the return is
    /// the new cursor, and <paramref name="slots"/> is mutated in place.
    /// </summary>
    public static ItemStack? Apply(ItemStack?[] slots, int index, ClickType click, ItemStack? cursor)
    {
        if (index < 0 || index >= slots.Length)
            return cursor;

        return click switch
        {
            ClickType.LeftClick => LeftClick(slots, index, cursor),
            ClickType.RightClick => RightClick(slots, index, cursor),
            _ => ShiftClick(slots, index, cursor),
        };
    }

    private static ItemStack? LeftClick(ItemStack?[] slots, int index, ItemStack? cursor)
    {
        var slot = slots[index];

        if (cursor is not { } held)
        {
            slots[index] = null;
            return slot;                                  // pick the whole stack up
        }

        if (slot is not { } target)
        {
            slots[index] = held;
            return null;                                  // put it down
        }

        if (!SameItem(target, held))
        {
            slots[index] = held;
            return target;                                // swap
        }

        // Same item: pour as much of the cursor into the slot as fits, keep the remainder.
        int room = MaxStackSize(target) - target.Count;
        if (room <= 0)
            return cursor;

        int moved = Math.Min(room, held.Count);
        slots[index] = target.WithCount(target.Count + moved);

        return held.Count > moved ? held.WithCount(held.Count - moved) : null;
    }

    private static ItemStack? RightClick(ItemStack?[] slots, int index, ItemStack? cursor)
    {
        var slot = slots[index];

        if (cursor is not { } held)
        {
            if (slot is not { } target)
                return null;

            // Take the larger half, so right-clicking a single item picks it up rather than nothing.
            int half = (target.Count + 1) / 2;
            slots[index] = target.Count > half ? target.WithCount(target.Count - half) : null;
            return target.WithCount(half);
        }

        if (slot is not { } existing)
        {
            slots[index] = held.WithCount(1);
            return Shrink(held);                          // place one
        }

        if (!SameItem(existing, held))
        {
            slots[index] = held;
            return existing;                              // swap
        }

        if (existing.Count >= MaxStackSize(existing))
            return cursor;

        slots[index] = existing.WithCount(existing.Count + 1);
        return Shrink(held);
    }

    /// Moves a stack BETWEEN regions - container to player inventory or back. Without the region
    /// test the item just shuffles around inside the region it started in.
    private static ItemStack? ShiftClick(ItemStack?[] slots, int index, ItemStack? cursor)
    {
        if (slots[index] is not { } moving)
            return cursor;

        int remaining = moving.Count;
        int max = MaxStackSize(moving);

        // Pass 0 tops up existing stacks, pass 1 fills empty slots. Merging first is what stops a
        // shift-click from scattering one item across several empty slots.
        for (int pass = 0; pass < 2 && remaining > 0; pass++)
        {
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (i == index || !CrossesRegion(slots.Length, index, i))
                    continue;

                if (pass == 0)
                {
                    if (slots[i] is not { } target || !SameItem(target, moving))
                        continue;

                    int room = max - target.Count;
                    if (room <= 0)
                        continue;

                    int move = Math.Min(room, remaining);
                    slots[i] = target.WithCount(target.Count + move);
                    remaining -= move;
                }
                else
                {
                    if (slots[i] != null)
                        continue;

                    int move = Math.Min(max, remaining);
                    slots[i] = moving.WithCount(move);
                    remaining -= move;
                }
            }
        }

        slots[index] = remaining > 0 ? moving.WithCount(remaining) : null;
        return cursor;                                    // shift-click never touches the cursor
    }

    // The container half is everything before the player's own slots; the layout puts it first.
    private static bool CrossesRegion(int totalSlots, int from, int to)
    {
        int containerSize = totalSlots - PlayerInventory.MAIN_SLOTS - PlayerInventory.HOTBAR_SLOTS;
        return from < containerSize != to < containerSize;
    }

    private static ItemStack? Shrink(ItemStack s) => s.Count > 1 ? s.WithCount(s.Count - 1) : null;

    public static bool SameItem(ItemStack a, ItemStack b) =>
        a.IsBlock == b.IsBlock && (a.IsBlock ? a.Block == b.Block : a.Item == b.Item);

    public static int MaxStackSize(ItemStack s) =>
        s.IsBlock ? BlockRegistry.Get(s.Block).MaxStackSize : ItemRegistry.Get(s.Item).MaxStackSize;
}
