// Holds a big enum of all the blocks, used for comparing and referencing different block types. Also, holds enum for different block rendering types, used to draw different shaped blocks. | DA | 2/5/26
namespace VoxelEngine.Terrain;

public enum BlockType : byte
{
    Air,
    Grass,
    Dirt,
    Stone,
    Wood,
    Leaves,
    Sand,
    Glowstone,
    Flower,
    Torch,
    Glass,
    Bedrock,
    Black,
    Blue,
    Bricks,
    BrownMushroom,
    CoalOre,
    DiamondOre,
    DuncanBlock,
    GoldOre,
    GrassTuft,
    Gravel,
    Green,
    IronOre,
    Planks,
    Red,
    RedMushroom,
    Sponge,
    White
}

public enum RenderingType : byte
{
    Normal,
    Cross,
    Torch
}
