// Main class used for saving and loading block entity data | DA | 3/5/26
using System.Collections.Generic;
using VoxelEngine.Items;
using VoxelEngine.Terrain;

namespace VoxelEngine.Saving;

/// <summary>
/// XML-friendly stand-in for <see cref="ItemStack"/>. ItemStack itself isn't used directly for XML serialization because it stores block/item type as enums tied to game registries rather than stable strings; this type stores the type name as text (<see cref="Type"/>) plus a flag for which enum it belongs to, so saves stay readable/robust across minor enum reordering.
/// </summary>
[Serializable]
public class SerializableStack
{
    public bool IsBlock;   // true = Type names a BlockType, false = Type names an ItemType
    public string Type = "";
    public int Count;

    /// <summary>Converts a live <see cref="ItemStack"/> into its serializable form for saving.</summary>
    public static SerializableStack From(ItemStack s) => new()
    {
        IsBlock = s.IsBlock,
        Type    = s.IsBlock ? s.Block.ToString() : s.Item.ToString(),
        Count   = s.Count,
    };

    /// <summary>Reconstructs the live <see cref="ItemStack"/> from saved data by parsing the stored type name back into the appropriate enum.</summary>
    public ItemStack ToItemStack()
    {
        if (IsBlock && Enum.TryParse<BlockType>(Type, out var block))
            return ItemStack.FromBlock(block, Count);
        if (!IsBlock && Enum.TryParse<ItemType>(Type, out var item))
            return ItemStack.FromItem(item, Count);
        return default;
    }
}

/// <summary>Serializable snapshot of a single furnace's position, slot contents, and burn/smelt progress.</summary>
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

/// <summary>Serializable snapshot of a single chest's position and non-empty inventory slots (empty slots are simply omitted from the list).</summary>
[Serializable]
public class SerializableChest
{
    public int X, Y, Z;
    public List<SerializableChestSlot> Slots = new();
}

/// <summary>One occupied inventory slot within a serialized chest/double chest, keyed by its slot index so order-independent XML round-trips correctly.</summary>
[Serializable]
public class SerializableChestSlot
{
    public int Index;
    public SerializableStack Stack = new();
}

/// <summary>Serializable snapshot of a single double chest's position and non-empty inventory slots.</summary>
[Serializable]
public class SerializableDoubleChest
{
    public int X, Y, Z;
    public List<SerializableChestSlot> Slots = new();
}

/// <summary>
/// Root object for the entire <c>block_entities.xml</c> file - the complete set of furnaces, chests, and double chests in a saved world. Written/read by <see cref="VoxelEngine.BlockEntities.BlockEntityManager"/>.
/// </summary>
[Serializable]
public class BlockEntityFile
{
    public List<SerializableFurnace> Furnaces = new();
    public List<SerializableChest> Chests = new();
    public List<SerializableDoubleChest> DoubleChests = new();
}
