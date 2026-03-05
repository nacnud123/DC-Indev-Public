// Main class used for saving and loading block entity data | DA | 3/5/26
using System.Collections.Generic;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.Saving;

[Serializable]
public class SerializableStack
{
    public bool IsBlock;
    public string Type = "";
    public int Count;

    public static SerializableStack From(ItemStack s) => new()
    {
        IsBlock = s.IsBlock,
        Type    = s.IsBlock ? s.Block.ToString() : s.Item.ToString(),
        Count   = s.Count,
    };

    public ItemStack ToItemStack()
    {
        if (IsBlock && Enum.TryParse<BlockType>(Type, out var block))
            return ItemStack.FromBlock(block, Count);
        if (!IsBlock && Enum.TryParse<ItemType>(Type, out var item))
            return ItemStack.FromItem(item, Count);
        return default;
    }
}

[Serializable]
public class SerializableFurnace
{
    public int X, Y, Z;
    public SerializableStack? Input;
    public SerializableStack? Fuel;
    public SerializableStack? Output;
    public int BurnTimeRemaining;
    public int SmeltProgress;
}

[Serializable]
public class SerializableChest
{
    public int X, Y, Z;
    public List<SerializableChestSlot> Slots = new();
}

[Serializable]
public class SerializableChestSlot
{
    public int Index;
    public SerializableStack Stack = new();
}

[Serializable]
public class BlockEntityFile
{
    public List<SerializableFurnace> Furnaces = new();
    public List<SerializableChest>  Chests   = new();
}
