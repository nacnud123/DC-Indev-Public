// This is a small struct that holds the world's data, like name, seed, ect... Importantly, this does not hold the world's chunk data that lives in its own file. | DA | 8/25/25 (Ported over from DuncanCraft2000)

using System.Collections.Generic;

namespace VoxelEngine.Saving;

// One serializable inventory slot. Type is either a BlockType or ItemType name string.
[Serializable]
public class SavedSlot
{
    public int Index;
    public bool IsBlock;
    public string Type = "";
    public int Count;
    public int Durability = -1;
}

[Serializable]
public class SavedPainting
{
    public int AnchorX;
    public int AnchorY;
    public int AnchorZ;
    public byte Facing;
    public string ArtName = "";
}

// Serializable snapshot of a world entity (mob or dropped item).
// Type is the entity class name: "Pig", "Sheep", "Zombie", "Skeleton", "Stalker", "DroppedItem".
[Serializable]
public class SavedEntity
{
    public string Type = "";
    public float X, Y, Z;
    public float Yaw;
    public int Health;

    // DroppedItemEntity only — null for mobs
    public SerializableStack? Stack;
}

[Serializable]
public class WorldSaveData
{
    public int ID;
    public string WorldName;
    public int Seed;
    public DateTime LastPlayed;
    public DateTime Created;
    public int WorldSize;

    public int WorldType;
    public int WorldTheme;
    public float WorldTime;

    public float PlayerX;
    public float PlayerY;
    public float PlayerZ;
    public float PlayerYaw;
    public float PlayerPitch;
    public bool HasPlayerPosition;

    public List<SavedSlot> Inventory = new();
    public List<SavedPainting> Paintings = new();
    public List<SavedEntity> Entities = new();
    public int PlayerHealth = -1; // -1 = not saved (use default max)
}