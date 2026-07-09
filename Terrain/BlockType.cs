// Holds a big enum of all the blocks, used for comparing and referencing different block types. Also, holds enum for different block rendering types, used to draw different shaped blocks. | DA | 2/5/26
namespace VoxelEngine.Terrain;

/// <summary>
/// Identifies every placeable block in the game. Backed by <see cref="byte"/> so it packs tightly into the per-chunk block arrays. Values are used as the lookup key for <c>BlockRegistry.Get(BlockType)</c>, which resolves the actual <c>Block</c> instance (hardness, texture, render type, tick behavior, etc.) for a given type. Grouped loosely by category via the comments below purely for readability; the numeric values are otherwise insignificant except that they must stay stable across saves (world files store raw byte IDs).
/// </summary>
public enum BlockType : byte
{
    Air = 0,

    // Natural / Surface
    Grass,
    Dirt,
    Sand,
    Gravel,
    GrassTuft,
    YellowFlower,
    RedFlower,
    BrownMushroom,
    RedMushroom,
    Sapling,
    Clay,
    Sandstone,
    Stoneslab,
    DoubleStoneslab,

    // Stone / Underground
    Stone,
    CobbleStone,
    MossyCobblestone,
    Bedrock,
    SpiderWeb,
    StoneStair,

    // Ores
    CoalOre,
    IronOre,
    GoldOre,
    DiamondOre,

    // Wood / Trees
    Wood,
    Leaves,
    Planks,
    WoodSlab,
    DoubleWoodSlab,
    WoodenStair,

    // Building
    Glass,
    Bricks,
    Glowstone,
    Sponge,
    Obsidian,
    Bookcase,
    DiamondBlock,
    GoldBlock,
    IronBlock,
    TNT,
    WorkBench,
    Furnace,
    FurnaceLit,
    Chest,
    DoubleChest,

    // Colored Blocks
    Black,
    Blue,
    Green,
    Red,
    White,

    // Torches
    Torch,

    // Fluids
    Water,
    Lava,

    // Special
    Fire,
    DuncanBlock,

    // Farming
    Farmland,
    WheatStage0,
    WheatStage1,
    WheatStage2,
    WheatStage3,
    WheatStage4,

    // Sentinel used by tools/commands (e.g. "give all", selection fill) to mean "every block type", not a real placeable block. Set to 255 to sit outside the normal contiguous ID range.
    All = 255
}

/// <summary>
/// Selects which mesh-generation strategy <see cref="ChunkMeshBuilder"/> uses for a block: a full 6-faced cube (Normal), an X-shaped pair of intersecting quads for foliage (Cross), a thin mounted quad-cluster (Torch), a half-height cube (Slab), an L-shaped/angled cube (Stair), or a custom flickering shape (Fire).
/// </summary>
public enum RenderingType : byte
{
    Normal,
    Cross,
    Torch,
    Slab,
    Stair,
    Fire
}

/// <summary>
/// Categorizes a block's break behavior/sound-and-particle set (dirt vs. stone vs. glass, etc.) so breaking effects and tool-effectiveness checks don't need to switch on the full <see cref="BlockType"/>.
/// </summary>
public enum BlockBreakMaterial : byte
{
    None,
    Dirt,
    Grass,
    Stone,
    Glass,
    Wool,
    Sand,
    Gravel,
    Wooden,
    Water,
    Lava
}
