// Main class used to represent items and their quantities in the game. | DA | 3/5/26
using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

public readonly struct ItemStack : IEquatable<ItemStack>
{
    public readonly bool IsBlock;
    public readonly BlockType Block;
    public readonly ItemType Item;
    public readonly int Count;
    public readonly int Durability;

    private ItemStack(bool isBlock, BlockType block, ItemType item, int count, int durability)
    {
        IsBlock = isBlock;
        Block = block;
        Item = item;
        Count = count;
        Durability = durability;
    }

    public static ItemStack FromBlock(BlockType b, int count = 1) => new(true, b, default, count, -1);

    public static ItemStack FromItem(ItemType i, int count = 1)
    {
        var def = ItemRegistry.Get(i);
        int dur = def.MaxDurability > 0 ? def.MaxDurability : -1;
        return new(false, default, i, count, dur);
    }

    public ItemStack WithCount(int count) => new(IsBlock, Block, Item, count, Durability);
    public ItemStack WithDurability(int value) => new(IsBlock, Block, Item, Count, value);

    public bool HasDurability => !IsBlock && Durability >= 0;

    public bool Equals(ItemStack other) => IsBlock == other.IsBlock && (!IsBlock || Block == other.Block) &&
                                           (IsBlock || Item == other.Item);

    public override bool Equals(object? obj) => obj is ItemStack s && Equals(s);
    public override int GetHashCode() => IsBlock ? Block.GetHashCode() : Item.GetHashCode();
    public static bool operator ==(ItemStack a, ItemStack b) => a.Equals(b);
    public static bool operator !=(ItemStack a, ItemStack b) => !a.Equals(b);
}