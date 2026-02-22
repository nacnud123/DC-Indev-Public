// Small class that holds data related to saving. | DA | 8/1/25 (Ported over from DuncanCraft2000)
using VoxelEngine.Terrain;

namespace VoxelEngine.Saving;

[Serializable]
public class Save
{
    public string[] FlatBlocks;
    public int SizeX, SizeY, SizeZ;
    public List<KeyValuePair<int, int>> Seed = new();

    public Save() { } // Needed for serialization, don't get rid of.

    public Save(Chunk chunk)
    {
        SizeX = Chunk.WIDTH;
        SizeY = Chunk.HEIGHT;
        SizeZ = Chunk.DEPTH;
        FlatBlocks = new string[Chunk.WIDTH * Chunk.HEIGHT * Chunk.DEPTH];

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for(int y = 0; y < Chunk.HEIGHT; y++)
            {
                for(int z = 0; z < Chunk.DEPTH; z++)
                {
                    int index = x + Chunk.WIDTH * (y + Chunk.HEIGHT * z);
                    FlatBlocks[index] = chunk.GetBlock(x, y, z).ToString();
                }
            }
        }
    }

    public string[,,] To3DArray()
    {
        var result = new string[SizeX, SizeY, SizeZ];
        for (int x = 0; x < SizeX; x++)
            for (int y = 0; y < SizeY; y++)
                for (int z = 0; z < SizeZ; z++)
                {
                    int index = x + SizeX * (y + SizeY * z);
                    result[x, y, z] = FlatBlocks[index];
                }
        return result;
    }
}
