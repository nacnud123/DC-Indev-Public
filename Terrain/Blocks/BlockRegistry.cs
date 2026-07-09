using System;
using System.Collections.Generic;

using VoxelEngine.Items;
using VoxelEngine.Rendering;

namespace VoxelEngine.Terrain.Blocks;

// Keeps exactly one instance of each Block subclass (one BlockGrass, one BlockStone, etc.) and looks them up by BlockType. Since a Chunk only stores a block's BlockType (a single byte per block, see Chunk.mBlocks), this registry is how code turns that byte back into the actual Block object with its properties/behavior. To add a new block: create the class, register it in the static constructor below, and add a matching value to the BlockType enum.
public static class BlockRegistry
{
    // One Block instance per BlockType, shared by the entire game - never per-placement. Keyed by the enum value stored in each Chunk's block byte array.
    private static readonly Dictionary<BlockType, Block> Blocks = new();

    // Runs once automatically, the first time anything touches BlockRegistry.
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
        Register(new BlockSandstone());
        Register(new BlockStoneSlab());
        Register(new BlockDoubleStoneSlab());

        // Stone
        Register(new BlockStone());
        Register(new BlockCobblestone());
        Register(new BlockMossyCobblestone());
        Register(new BlockBedrock());
        Register(new BlockStoneStair());

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
        Register(new BlockDoubleWoodSlab());
        Register(new WoodStairs());

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
        Register(new BlockObsidian());
        Register(new BlockBookcase());
        Register(new BlockDiamondBlock());
        Register(new BlockGoldBlock());
        Register(new BlockIronBlock());
        Register(new BlockTNT());
        Register(new BlockWorkBench());
        Register(new BlockFurnace());
        Register(new BlockFurnaceLit());
        Register(new BlockChest());
        Register(new BlockDoubleChest());

        // Colored
        Register(new BlockBlack());
        Register(new BlockBlue());
        Register(new BlockGreen());
        Register(new BlockRed());
        Register(new BlockWhite());

        // Lighting
        Register(new BlockTorch());

        // Fluids
        Register(new BlockWater());
        Register(new BlockLava());

        // Special
        Register(new BlockFire());

        // Farming
        Register(new BlockFarmland());
        Register(new BlockWheatStage0());
        Register(new BlockWheatStage1());
        Register(new BlockWheatStage2());
        Register(new BlockWheatStage3());
        Register(new BlockWheatStage4());
    }

    /// <summary>Adds (or replaces) the singleton instance for block.Type. Called once per block class
    /// from the static constructor above; not expected to be called again at runtime.</summary>
    public static void Register(Block block)
    {
        Blocks[block.Type] = block;
    }

    /// <summary>Central lookup: turns a stored BlockType byte back into its shared Block instance so
    /// callers can read its properties or invoke its tick/interaction callbacks. Throws if the type
    /// was never registered (should only happen for a bug - every BlockType enum value must be registered).</summary>
    public static Block Get(BlockType type)
    {
        if (Blocks.TryGetValue(type, out var block))
            return block;

        throw new ArgumentException($"Unknown block type: {type}");
    }

    /// <summary>Enumerates every registered block instance, e.g. for building the creative inventory list.</summary>
    public static IEnumerable<Block> GetAll() => Blocks.Values;

    // The following are thin BlockType-keyed convenience wrappers around Get(type).<Property>, letting callers avoid an explicit BlockRegistry.Get(type) at every call site.
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
    public static bool IsFlamable(BlockType type) => Get(type).IsFlamable;
    public static string GetName(BlockType type) => Get(type).Name;
    public static TextureCoords GetTopTexture(BlockType type) => Get(type).TopTextureCoords;
    public static TextureCoords GetBottomTexture(BlockType type) => Get(type).BottomTextureCoords;
    public static TextureCoords GetSideTexture(BlockType type) => Get(type).SideTextureCoords;

    public static TextureCoords GetFrontTexture(BlockType type) => Get(type).FrontTextureCoords;
    public static TextureCoords GetBackTexture(BlockType type) => Get(type).BackTextureCoords;
    public static TextureCoords GetLeftTexture(BlockType type) => Get(type).LeftTextureCoords;
    public static TextureCoords GetRightTexture(BlockType type) => Get(type).RightTextureCoords;

    public static TextureCoords GetParticleTexture(BlockType type) => Get(type).InventoryTextureCoords;
    public static RenderingType GetRenderType(BlockType type) => Get(type).RenderType;
    public static Vector3 GetBoundsMin(BlockType type) => Get(type).BoundsMin;
    public static Vector3 GetBoundsMax(BlockType type) => Get(type).BoundsMax;
    public static bool NeedsSupportBelow(BlockType type) => Get(type).NeedsSupportBelow;
    public static bool IsFluid(BlockType type) => Get(type).IsFluid;
    public static bool TicksRandomly(BlockType type) => Get(type).TicksRandomly;
    public static bool CanBlockSupport(BlockType type, BlockType beneath) => Get(type).CanBlockSupport(beneath);
    public static BlockBreakMaterial GetBlockBreakMaterial(BlockType type) => Get(type).BreakMaterial;
    public static int GetTickRate(BlockType type) => Get(type).TickRate;
    public static ItemStack? GetDrop(BlockType type, byte metaData = 0) => Get(type).GetDrop(metaData);
}
