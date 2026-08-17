using System.Collections;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Decorative grass tuft (tall grass) block. Non-solid cross-sprite that requires
/// grass or dirt beneath it. Unlike most decoration blocks it has a chance to drop
/// wheat seeds when removed, mirroring the classic "trample tall grass for seeds" mechanic.
/// </summary>
public class BlockGrassTuft : Block
{
    public override BlockType Type => BlockType.GrassTuft;
    public override string Name => "Grass Tuft";
    public override RenderingType RenderType => RenderingType.Cross;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Grass;
    public override bool IsFlamable => true;
    public override bool IsSolid => false;
    public override bool IsReplaceable => true;
    public override float Hardness => 0.0f;
    // No direct item drop from breaking - seed drops are instead handled probabilistically
    // in OnRemoved below, so a normal break never yields anything via GetDrop.
    public override ItemStack? GetDrop(byte metadata) => null;
    public override int LightOpacity => 0;
    public override bool SuffocatesBeneath => true;
    public override bool NeedsSupportBelow => true;
    public override List<BlockType> BlocksThatCanSupport => [BlockType.Grass, BlockType.Dirt];

    // Single shared tile - cross-sprite rendering draws only one texture per block.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    /// <summary>
    /// Called whenever the tuft is removed (broken, washed away, trampled, etc.).
    /// 1-in-5 chance to spawn 1-3 dropped Seeds item entities scattered randomly
    /// within the block's footprint, so tall grass acts as a seed source.
    /// </summary>
    public override void OnRemoved(World world, int x, int y, int z)
    {
        if (GameContext.Current.GameRandom.Next(5) == 0)
        {
            int seedCount = GameContext.Current.GameRandom.Next(1, 4);

            for (int i = 0; i < seedCount; i++)
            {
                var rng = GameContext.Current.GameRandom;
                // Jitter the drop position within the block's horizontal footprint
                // (0.15-0.85 on x/z) and near the bottom of the block vertically (0.1-0.4)
                // so seeds don't all spawn stacked at the block origin.
                float sx = x + (float)rng.NextDouble() * 0.7f + 0.15f;
                float sy = y + (float)rng.NextDouble() * 0.3f + 0.1f;
                float sz = z + (float)rng.NextDouble() * 0.7f + 0.15f;
                world.AddEntity(new GameEntity.DroppedItemEntity(
                    new Vector3(sx, sy, sz), ItemStack.FromItem(ItemType.Seeds)));
            }
        }
    }
}
