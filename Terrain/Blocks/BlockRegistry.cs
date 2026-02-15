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
        // Special
        Register(new BlockAir());
        Register(new BlockDuncanBlock());

        // Natural
        Register(new BlockGrass());
        Register(new BlockDirt());
        Register(new BlockSand());
        Register(new BlockGravel());
        Register(new BlockClay());

        // Stone
        Register(new BlockStone());
        Register(new BlockCobblestone());
        Register(new BlockMossyCobblestone());
        Register(new BlockBedrock());

        // Ores
        Register(new BlockCoalOre());
        Register(new BlockIronOre());
        Register(new BlockGoldOre());
        Register(new BlockDiamondOre());

        // Wood
        Register(new BlockWood());
        Register(new BlockLeaves());
        Register(new BlockPlanks());
        Register(new BlockWoodSlab());

        // Decoration
        Register(new BlockFlower());
        Register(new BlockRedFlower());
        Register(new BlockGrassTuft());
        Register(new BlockBrownMushroom());
        Register(new BlockRedMushroom());
        Register(new BlockSpiderweb());
        Register(new BlockSapling());

        // Building
        Register(new BlockGlass());
        Register(new BlockBricks());
        Register(new BlockSponge());
        Register(new BlockGlowstone());

        // Colored
        Register(new BlockBlack());
        Register(new BlockBlue());
        Register(new BlockGreen());
        Register(new BlockRed());
        Register(new BlockWhite());

        // Lighting
        Register(new BlockTorch());
        Register(new BlockWallTorch(BlockType.TorchNorth, "Wall Torch (North)", 0));
        Register(new BlockWallTorch(BlockType.TorchSouth, "Wall Torch (South)", 1));
        Register(new BlockWallTorch(BlockType.TorchEast, "Wall Torch (East)", 2));
        Register(new BlockWallTorch(BlockType.TorchWest, "Wall Torch (West)", 3));

        // Fluids
        Register(new BlockWater());
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
    public static float GetHardness(BlockType type) => Get(type).Hardness;
    public static bool ShowInInventory(BlockType type) => Get(type).ShowInInventory;
    public static bool BlocksLight(BlockType type) => Get(type).BlocksLight;
    public static int GetBlockOpacity(BlockType type) => Get(type).LightOpacity;
    public static bool GetSuffocatesBeneath(BlockType type) => Get(type).SuffocatesBeneath;
    public static bool IsTransparent(BlockType type) => type == BlockType.Air || Get(type).IsTransparent;
    public static bool GetSlowsEntity(BlockType type) => Get(type).SlowsEntities;
    public static string GetName(BlockType type) => Get(type).Name;
    public static TextureCoords GetTopTexture(BlockType type) => Get(type).TopTextureCoords;
    public static TextureCoords GetBottomTexture(BlockType type) => Get(type).BottomTextureCoords;
    public static TextureCoords GetSideTexture(BlockType type) => Get(type).SideTextureCoords;
    public static TextureCoords GetParticleTexture(BlockType type) => Get(type).InventoryTextureCoords;
    public static RenderingType GetRenderType(BlockType type) => Get(type).RenderType;
    public static Vector3 GetBoundsMin(BlockType type) => Get(type).BoundsMin;
    public static Vector3 GetBoundsMax(BlockType type) => Get(type).BoundsMax;
    public static bool NeedsSupportBelow(BlockType type) => Get(type).NeedsSupportBelow;
    public static bool TicksRandomly(BlockType type) => Get(type).TicksRandomly;
    public static bool CanBlockSupport(BlockType type, BlockType beneath) => Get(type).CanBlockSupport(beneath);
    public static BlockBreakMaterial GetBlockBreakMaterial(BlockType type) => Get(type).BreakMaterial;
}
