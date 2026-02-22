using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

// Metadata: 0=ground, 1=North(facing 0), 2=South(facing 1), 3=East(facing 2), 4=West(facing 3)
public class BlockTorch : Block
{
    private const float OFFSET = 7f / 16f;
    private const float SIZE = 2f / 16f;
    private const float HEIGHT = 10f / 16f;

    public override BlockType Type => BlockType.Torch;
    public override string Name => "Torch";
    public override RenderingType RenderType => RenderingType.Torch;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;

    public override bool IsSolid => false;
    public override int LightOpacity => 0;
    public override int LightEmission => 14;
    public override float Hardness => 0.0f;
    public override bool SuffocatesBeneath => true;
    public override bool NeedsSupportBelow => true;

    // Default bounds (ground torch)
    public override Vector3 BoundsMin => new(OFFSET, 0, OFFSET);
    public override Vector3 BoundsMax => new(OFFSET + SIZE, HEIGHT, OFFSET + SIZE);

    // Wall torch bounds by facing (0=North, 1=South, 2=East, 3=West)
    public static (Vector3 min, Vector3 max) GetWallTorchBounds(int facing) => facing switch
    {
        0 => (new(6f / 16f, 3f / 16f, 0f), new(10f / 16f, 13f / 16f, 6f / 16f)),
        1 => (new(6f / 16f, 3f / 16f, 10f / 16f), new(10f / 16f, 13f / 16f, 1f)),
        2 => (new(10f / 16f, 3f / 16f, 6f / 16f), new(1f, 13f / 16f, 10f / 16f)),
        3 => (new(0f, 3f / 16f, 6f / 16f), new(6f / 16f, 13f / 16f, 10f / 16f)),
        _ => (new(OFFSET, 0, OFFSET), new(OFFSET + SIZE, HEIGHT, OFFSET + SIZE))
    };

    private static readonly Vector3[] WallParticleOffsets =
    [
        new(0f, 0f, -0.25f),   // North
        new(0f, 0f,  0.25f),   // South
        new(0.25f, 0f, 0f),    // East
        new(-0.25f, 0f, 0f)    // West
    ];

    public override TextureCoords TopTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 7, 2, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 10);
    public override TextureCoords InventoryTextureCoords => UvHelper.FromTileCoords(6, 0);

    public override void RandomDisplayTick(int x, int y, int z, Random random)
    {
        int meta = World.Current?.GetMetadata(x, y, z) ?? 0;
        if (meta == 0)
            Game.Instance?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z));
        else
            Game.Instance?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z) + WallParticleOffsets[meta - 1]);
    }

    public override bool CanBlockSupport(BlockType beneath) => beneath != BlockType.Glass;
}
