
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockLeaves : Block
{
    public override BlockType Type => BlockType.Leaves;
    public override string Name => "Leaves";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Grass;
    public override bool IsFlamable => true;
    public override float Hardness => 0.2f;
    public override bool TicksRandomly => true;
    public override int LightOpacity => 1;

    // 1-in-200 chance to drop an apple; otherwise 1-in-10 chance to drop a sapling
    public override ItemStack? GetDrop(byte metadata)
    {
        var rng = Game.Instance.GameRandom;
        if (rng.Next(200) == 0) 
            return ItemStack.FromItem(ItemType.Apple);
        
        if (rng.Next(100) == 0) 
            return ItemStack.FromBlock(BlockType.Sapling);
        
        return null;
    }

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(1, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    public override void RandomTick(World world, int x, int y, int z, Random random)
    {
        if (BlockRegistry.IsSolid(world.GetBlock(x, y - 1, z)))
            return;

        for (int bx = x - 2; bx <= x + 2; bx++)
        {
            for (int by = y - 1; by <= y + 1; by++)
            {
                for (int bz = z - 2; bz <= z + 2; bz++)
                {
                    if (world.GetBlock(bx, by, bz) == BlockType.Wood)
                        return;
                }
            }
        }
        
        var drop = GetDrop(0);
        if (drop.HasValue)
        {
            var spawnPos = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
            world.AddEntity(new DroppedItemEntity(spawnPos, drop.Value, Game.Instance.WorldTexture));
        }

        Game.Instance.ParticleSystem.SpawnBlockBreakParticles(new Vector3(x, y, z), BlockType.Leaves);
        world.SetBlock(x, y, z, BlockType.Air);
    }
}