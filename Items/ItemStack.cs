// Main class used to represent items and their quantities in the game. | DA | 3/5/26
using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

/// <summary>
/// Immutable value type representing "N of a thing" in an inventory slot. A single stack can hold either a placeable block (<see cref="IsBlock"/> true, using <see cref="Block"/>) or a non-block item (using <see cref="Item"/>) — never both — which is why blocks and items share one slot representation throughout the inventory/crafting/UI code. Being a readonly struct, any "mutation" (changing count/durability) returns a new instance via the With* helpers.
/// </summary>
public readonly struct ItemStack : IEquatable<ItemStack>
{
    /// <summary>Discriminator: true if this stack represents a block, false if it represents an item.</summary>
    public readonly bool IsBlock;

    /// <summary>Valid only when IsBlock is true.</summary>
    public readonly BlockType Block;

    /// <summary>Valid only when IsBlock is false.</summary>
    public readonly ItemType Item;

    /// <summary>Quantity currently in the stack.</summary>
    public readonly int Count;

    /// <summary>Remaining durability for tools/armor; -1 for stacks that don't track durability (blocks, non-tool items).</summary>
    public readonly int Durability;

    private ItemStack(bool isBlock, BlockType block, ItemType item, int count, int durability)
    {
        IsBlock = isBlock;
        Block = block;
        Item = item;
        Count = count;
        Durability = durability;
    }

    /// <summary>Creates a stack of a placeable block. Blocks never have durability, so it's fixed at -1.</summary>
    public static ItemStack FromBlock(BlockType b, int count = 1) => new(true, b, default, count, -1);

    /// <summary>
    /// Creates a stack of a non-block item, looking up its definition to seed starting durability at the item's MaxDurability (or -1 if the item type has no durability, e.g. resources/food).
    /// </summary>
    public static ItemStack FromItem(ItemType i, int count = 1)
    {
        var def = ItemRegistry.Get(i);
        int dur = def.MaxDurability > 0 ? def.MaxDurability : -1;
        return new(false, default, i, count, dur);
    }

    /// <summary>Returns a copy of this stack with a different count (e.g. after splitting/merging in the UI).</summary>
    public ItemStack WithCount(int count) => new(IsBlock, Block, Item, count, Durability);

    /// <summary>Returns a copy of this stack with updated durability (e.g. after a tool use or repair).</summary>
    public ItemStack WithDurability(int value) => new(IsBlock, Block, Item, Count, value);

    /// <summary>True for item stacks that track durability (tools/armor); always false for blocks.</summary>
    public bool HasDurability => !IsBlock && Durability >= 0;

    // Equality/stacking compatibility is based on the block/item type only — Count and Durability are intentionally excluded so slots with differing counts (or partially-worn tools) can still be recognized as "the same kind of thing" for stacking/merging purposes.
    public bool Equals(ItemStack other) => IsBlock == other.IsBlock && (!IsBlock || Block == other.Block) &&
                                           (IsBlock || Item == other.Item);

    public override bool Equals(object? obj) => obj is ItemStack s && Equals(s);
    public override int GetHashCode() => IsBlock ? Block.GetHashCode() : Item.GetHashCode();
    public static bool operator ==(ItemStack a, ItemStack b) => a.Equals(b);
    public static bool operator !=(ItemStack a, ItemStack b) => !a.Equals(b);
}