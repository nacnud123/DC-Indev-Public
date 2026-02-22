// Holds a big enum of all the blocks, used for comparing and referencing different block types. Also, holds enum for different block rendering types, used to draw different shaped blocks. | DA | 2/5/26
namespace VoxelEngine.Terrain;

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
    Chest,

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
    

    All = 255
}

public enum RenderingType : byte
{
    Normal,
    Cross,
    Torch,
    Slab,
    Stair,
    Fire
}

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
