using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockTorch : Block
{
    private const float OFFSET = 7f / 16f;
    private const float SIZE = 2f / 16f;
    private const float HEIGHT = 10f / 16f;

    public override BlockType Type => BlockType.Torch;
    public override string Name => "Torch";
    public override RenderingType RenderType => RenderingType.Torch;

    public override bool IsSolid => false;
    public override int LightOpacity => 0;
    public override int LightEmission => 14;
    public override float Hardness => 0.0f;
    public override bool SuffocatesBeneath => true;

    public override Vector3 BoundsMin => new(OFFSET, 0, OFFSET);
    public override Vector3 BoundsMax => new(OFFSET + SIZE, HEIGHT, OFFSET + SIZE);

    public override TextureCoords TopTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 7, 2, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 10);
    public override TextureCoords InventoryTextureCoords => UvHelper.FromTileCoords(6, 0);

    public override void RandomDisplayTick(int x, int y, int z, Random random)
    {
        Game.Instance?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z));
    }
}
