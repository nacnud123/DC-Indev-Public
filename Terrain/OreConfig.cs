// Small file that holds the struct for ore configs | DA | 2/14/26
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VoxelEngine.Terrain;

public struct OreConfig
{
    public BlockType Type;
    public int MinY;
    public int MaxY;
    public float ChunkChance;
    public int VeinsPerChunk;
    public float MinRadius;
    public float MaxRadius;
    public int MinLength;
    public int MaxLength;
}
