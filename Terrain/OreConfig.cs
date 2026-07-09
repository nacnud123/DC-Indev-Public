// Small file that holds the struct for ore configs | DA | 2/14/26
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VoxelEngine.Terrain;

/// <summary>
/// Declarative per-ore-type generation parameters describing how a vein of this ore should be scattered through the world: valid height range, how often it spawns, how many veins per chunk, and the vein's worm-carve radius/length bounds. Note: TerrainGen's current GenerateOreType takes these same knobs (abundance, sizeScale, maxY) as individual parameters per call rather than consuming OreConfig instances directly - this struct documents the shape of that data.
/// </summary>
public struct OreConfig
{
    // Block placed for this ore (e.g. CoalOre, IronOre).
    public BlockType Type;
    // Inclusive world-Y range this ore is allowed to generate within (deeper ores get lower MaxY, e.g. diamonds).
    public int MinY;
    public int MaxY;
    // Probability [0,1] that a given chunk attempts to spawn a vein of this ore at all.
    public float ChunkChance;
    // How many separate veins to place per chunk when generation is attempted.
    public int VeinsPerChunk;
    // Bounds on the worm-carve radius used when tunneling out each vein's ore blocks (bigger radius = fatter/rarer veins).
    public float MinRadius;
    public float MaxRadius;
    // Bounds on how many steps (blocks long) a single vein's carve path runs.
    public int MinLength;
    public int MaxLength;
}
