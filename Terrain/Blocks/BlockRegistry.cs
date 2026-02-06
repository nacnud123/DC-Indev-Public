using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using VoxelEngine.Rendering;

namespace VoxelEngine.Terrain.Blocks;

public static class BlockRegistry
{
    private static readonly Dictionary<BlockType, Block> Blocks = new();

    static BlockRegistry()
    {
        Register(new BlockAir());
        Register(new BlockGrass());
        Register(new BlockDirt());
        Register(new BlockStone());
        Register(new BlockWood());
        Register(new BlockLeaves());
        Register(new BlockSand());
        Register(new BlockGlowstone());
        Register(new BlockFlower());
        Register(new BlockTorch());
        Register(new BlockGlass());
        Register(new BlockBedrock());
        Register(new BlockBlack());
        Register(new BlockBlue());
        Register(new BlockBricks());
        Register(new BlockBrownMushroom());
        Register(new BlockCoalOre());
        Register(new BlockDiamondOre());
        Register(new BlockDuncanBlock());
        Register(new BlockGoldOre());
        Register(new BlockGrassTuft());
        Register(new BlockGravel());
        Register(new BlockGreen());
        Register(new BlockIronOre());
        Register(new BlockPlanks());
        Register(new BlockRed());
        Register(new BlockRedMushroom());
        Register(new BlockSponge());
        Register(new BlockWhite());
    }

    public static void Register(Block block)
    {
        Blocks[block.Type] = block;
    }

    public static Block Get(BlockType type)
    {
        if (Blocks.TryGetValue(type, out var block))
            return block;

        throw new ArgumentException($"Unknown block type: {type}");
    }

    public static IEnumerable<Block> GetAll() => Blocks.Values;

    public static bool IsSolid(BlockType type) => Get(type).IsSolid;
    public static bool IsGravityBlock(BlockType type) => Get(type).GravityBlock;
    public static bool IsBreakable(BlockType type) => Get(type).IsBreakable;
    public static bool ShowInInventory(BlockType type) => Get(type).ShowInInventory;
    public static bool BlocksLight(BlockType type) => Get(type).BlocksLight;
    public static int GetBlockOpacity(BlockType type) => Get(type).LightOpacity;
    public static bool GetSuffocatesBeneath(BlockType type) => Get(type).SuffocatesBeneath;
    public static bool IsTransparent(BlockType type) => type == BlockType.Air || Get(type).IsTransparent;
    public static string GetName(BlockType type) => Get(type).Name;
    public static TextureCoords GetTopTexture(BlockType type) => Get(type).TopTextureCoords;
    public static TextureCoords GetBottomTexture(BlockType type) => Get(type).BottomTextureCoords;
    public static TextureCoords GetSideTexture(BlockType type) => Get(type).SideTextureCoords;
    public static TextureCoords GetParticleTexture(BlockType type) => Get(type).InventoryTextureCoords;
    public static RenderingType GetRenderType(BlockType type) => Get(type).RenderType;
    public static Vector3 GetBoundsMin(BlockType type) => Get(type).BoundsMin;
    public static Vector3 GetBoundsMax(BlockType type) => Get(type).BoundsMax;
    public static bool TicksRandomly(BlockType type) => Get(type).TicksRandomly;
}
