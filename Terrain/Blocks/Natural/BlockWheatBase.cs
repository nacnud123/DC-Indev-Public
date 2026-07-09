using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;

namespace VoxelEngine.Terrain.Blocks;

public abstract class BlockWheatBase : Block
{
    public abstract int Stage { get; }
    public abstract BlockType NextStage { get; }

    public override string Name => "Wheat";
    public override RenderingType RenderType => RenderingType.Cross;
    public override bool IsSolid => false;
    public override bool IsReplaceable => false;
    public override bool ShowInInventory => false;
    public override bool NeedsSupportBelow => true;
    public override bool CrossHasOffset => false;
    public override float Hardness => 0f;
    public override int LightOpacity => 0;
    public override bool TicksRandomly => Stage < 4;

    public override List<BlockType> BlocksThatCanSupport =>
        new List<BlockType> { BlockType.Farmland };

    public override void RandomTick(World world, int x, int y, int z, Random random)
    {
        var below = world.GetBlock(x, y - 1, z);
        if (below != BlockType.Farmland)
        {
            world.SetBlock(x, y, z, BlockType.Air);
            world.SetChunkAsModified(x, y, z);
            SpawnDrop(world, x, y, z, ItemStack.FromItem(ItemType.Seeds));
            return;
        }

        if (random.Next(3) == 0)
        {
            world.SetBlock(x, y, z, NextStage);
            world.SetChunkAsModified(x, y, z);
        }
    }

    public override ItemStack? GetDrop(byte metadata)
    {
        // Drop is handled via OnRemoved / RandomTick for multi-drop. For tool breaking, return seeds as fallback.
        return ItemStack.FromItem(ItemType.Seeds);
    }

    protected void SpawnDrop(World world, int x, int y, int z, ItemStack stack)
    {
        var rng = Game.Instance.GameRandom;
        float sx = x + (float)rng.NextDouble() * 0.7f + 0.15f;
        float sy = y + (float)rng.NextDouble() * 0.3f + 0.1f;
        float sz = z + (float)rng.NextDouble() * 0.7f + 0.15f;
        world.AddEntity(new GameEntity.DroppedItemEntity(
            new Vector3(sx, sy, sz), stack, Game.Instance.WorldTexture));
    }
}
