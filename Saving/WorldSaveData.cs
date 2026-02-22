// This is a small struct that holds the world's data, like name, seed, ect... Importantly, this does not hold the world's chunk data that lives in its own file. | DA | 8/25/25 (Ported over from DuncanCraft2000)
namespace VoxelEngine.Saving;

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
}
