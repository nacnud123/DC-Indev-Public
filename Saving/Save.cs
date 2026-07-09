// Small class that holds data related to saving. | DA | 8/1/25 (Ported over from DuncanCraft2000)
using VoxelEngine.Terrain;

namespace VoxelEngine.Saving;

/// <summary>
/// Legacy/alternate representation of a chunk's block data as a flat array of block type name strings (rather than the compact binary format used by <see cref="Serialization.SaveChunk"/>/<see cref="Serialization.Load"/>). Kept for structure import/export style workflows (ported from DuncanCraft2000) where a human-readable, XML/JSON-friendly string array is more convenient than the packed binary chunk format.
/// </summary>
[Serializable]
public class Save
{
    // 3D block grid flattened into 1D; index math must match To3DArray/constructor exactly.
    public string[] FlatBlocks;
    public int SizeX, SizeY, SizeZ;
    public List<KeyValuePair<int, int>> Seed = new();

    public Save() { } // Needed for serialization, don't get rid of.

    /// <summary>Snapshots an entire chunk's blocks into a flat array of block type names, in x + width*(y + height*z) order.</summary>
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
                    // Flatten (x, y, z) into a single index; matches the unpacking in To3DArray.
                    int index = x + Chunk.WIDTH * (y + Chunk.HEIGHT * z);
                    FlatBlocks[index] = chunk.GetBlock(x, y, z).ToString();
                }
            }
        }
    }

    /// <summary>Unflattens <see cref="FlatBlocks"/> back into a 3D array of block type name strings, indexed [x, y, z].</summary>
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
