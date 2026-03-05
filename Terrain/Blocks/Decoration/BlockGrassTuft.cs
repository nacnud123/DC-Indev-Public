using System.Collections;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

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
    public override ItemStack? GetDrop(byte metadata) => null;
    public override int LightOpacity => 0;
    public override bool SuffocatesBeneath => true;
    public override bool NeedsSupportBelow => true;
    public override List<BlockType> BlocksThatCanSupport => [BlockType.Grass, BlockType.Dirt];

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    public override void OnRemoved(World world, int x, int y, int z)
    {
        if (Game.Instance.GameRandom.Next(5) == 0)
        {
            int seedCount = Game.Instance.GameRandom.Next(1, 4);

            for (int i = 0; i < seedCount; i++)
            {
                var rng = Game.Instance.GameRandom;
                float sx = x + (float)rng.NextDouble() * 0.7f + 0.15f;
                float sy = y + (float)rng.NextDouble() * 0.3f + 0.1f;
                float sz = z + (float)rng.NextDouble() * 0.7f + 0.15f;
                world.AddEntity(new GameEntity.DroppedItemEntity(
                    new OpenTK.Mathematics.Vector3(sx, sy, sz), ItemStack.FromItem(ItemType.Seeds), Game.Instance.WorldTexture));
            }
        }
    }
}
