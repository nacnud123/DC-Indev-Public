using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Sapling block: a non-solid cross-sprite placed on grass/dirt that, on a random world
/// tick, has a chance to grow into a full tree via <see cref="World.GrowTree"/>. Growth
/// requires clear air directly above so the tree isn't blocked immediately as it starts.
/// </summary>
public class BlockSapling : Block
{
    public override BlockType Type => BlockType.Sapling;
    public override string Name => "Sapling";
    public override RenderingType RenderType => RenderingType.Cross;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Dirt;
    public override bool IsFlamable => true;
    public override bool IsSolid => false;
    public override int LightOpacity => 0;
    public override float Hardness => 0.0f;
    // Opts into the world's random-tick system so RandomTick below fires periodically,
    // driving the sapling's chance to grow each in-game tick cycle.
    public override bool TicksRandomly => true;
    public override bool SuffocatesBeneath => true;
    public override bool NeedsSupportBelow => true;
    public override List<BlockType> BlocksThatCanSupport => new List<BlockType>() { BlockType.Grass, BlockType.Dirt };

    // Single shared tile - cross-sprite rendering draws only one texture per block.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    /// <summary>
    /// Fired occasionally by the world's random-tick scheduler. Requires open air directly
    /// above the sapling (so a tree wouldn't instantly collide with something), then rolls
    /// a 1-in-5 chance per tick to actually grow into a tree.
    /// </summary>
    public override void RandomTick(World world, int x, int y, int z, Random random)
    {
        if (world.GetBlock(x, y + 1, z) != BlockType.Air)
            return;

        if (random.Next(5) != 0)
            return;

        world.GrowTree(x, y, z);
    }
}