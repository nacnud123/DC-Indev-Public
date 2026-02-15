using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockWallTorch(BlockType type, string name, int facing) : Block
{
    private int Facing { get; } = facing;

    public override BlockType Type => type;
    public override string Name => name;
    public override RenderingType RenderType => RenderingType.Torch;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;

    public override bool IsSolid => false;
    public override int LightOpacity => 0;
    public override int LightEmission => 14;
    public override float Hardness => 0.0f;
    public override bool SuffocatesBeneath => true;
    public override bool ShowInInventory => false;

    public override Vector3 BoundsMin => Facing switch
    {
        0 => new(6f / 16f, 3f / 16f, 0f),
        1 => new(6f / 16f, 3f / 16f, 10f / 16f),
        2 => new(10f / 16f, 3f / 16f, 6f / 16f),
        3 => new(0f, 3f / 16f, 6f / 16f),
        _ => Vector3.Zero
    };

    public override Vector3 BoundsMax => Facing switch
    {
        0 => new(10f / 16f, 13f / 16f, 6f / 16f),
        1 => new(10f / 16f, 13f / 16f, 1f),
        2 => new(1f, 13f / 16f, 10f / 16f),
        3 => new(6f / 16f, 13f / 16f, 10f / 16f),
        _ => Vector3.One
    };
    
    private readonly Vector3[] mParticleOffsets =
    [
        new Vector3(0f, 0f, -0.25f),
        new Vector3(0f, 0f,  0.25f),
        new Vector3(0.25f, 0f, 0f),
        new Vector3(-0.25f, 0f, 0f)
    ];

    public override TextureCoords TopTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 7, 2, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 10);
    public override TextureCoords InventoryTextureCoords => UvHelper.FromTileCoords(6, 0);

    public override void RandomDisplayTick(int x, int y, int z, Random random)
    {
        Game.Instance?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z) + mParticleOffsets[Facing]);
    }

    public override bool CanBlockSupport(BlockType beneath) => beneath != BlockType.Glass;
}
