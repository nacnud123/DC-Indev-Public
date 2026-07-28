
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

// Metadata: 0=ground, 1=North(facing 0), 2=South(facing 1), 3=East(facing 2), 4=West(facing 3)
/// <summary>
/// Standing (ground-placed) torch block. This is the canonical example in the codebase
/// of a block that encodes placement orientation in its 1-byte metadata value:
/// metadata 0 means "standing upright on the ground" (the default case, handled entirely
/// by this class's default bounds/UV), while metadata 1-4 mean "was placed against a
/// wall, facing North/South/East/West respectively" - written by placement code when a
/// torch is placed against a vertical surface with no floor support. Note that even
/// though a torch's BlockType stays Torch with wall-facing metadata for gameplay/lookup
/// purposes, the actual rendered/collidable wall torch uses a *separate* BlockType and
/// class (<see cref="BlockWallTorch"/>), one instance per facing, constructed with a
/// zero-based `facing` (0=N,1=S,2=E,3=W) - i.e. BlockWallTorch's facing = this class's
/// metadata - 1. <see cref="GetWallTorchBounds"/> below is the zero-based (facing)
/// lookup table that BlockWallTorch.BoundsMin/BoundsMax mirror, and
/// <see cref="RandomDisplayTick"/> converts this block's 1-4 metadata down to that same
/// zero-based index (`meta - 1`) to pick a matching smoke-particle offset.
/// </summary>
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

    // Default bounds (ground torch): a thin 2/16-wide post centered near the middle of
    // the block, standing from the floor (y=0) up to 10/16 block height.
    public override Vector3 BoundsMin => new(OFFSET, 0, OFFSET);
    public override Vector3 BoundsMax => new(OFFSET + SIZE, HEIGHT, OFFSET + SIZE);

    /// <summary>
    /// Lookup table of collision bounds for a wall-mounted torch by zero-based facing
    /// (0=North, 1=South, 2=East, 3=West - i.e. metadata-1 for a torch placed on a wall).
    /// Each case offsets a thin box out from the wall on the appropriate axis/side so the
    /// torch visually juts out from the surface it's stuck to, angled up at 3/16 from the
    /// floor to 13/16 height. Falls back to the default ground-torch bounds for any other
    /// value (defensive default; should not occur for valid wall-torch facings 0-3).
    /// </summary>
    public static (Vector3 min, Vector3 max) GetWallTorchBounds(int facing) => facing switch
    {
        0 => (new(6f / 16f, 3f / 16f, 0f), new(10f / 16f, 13f / 16f, 6f / 16f)),
        1 => (new(6f / 16f, 3f / 16f, 10f / 16f), new(10f / 16f, 13f / 16f, 1f)),
        2 => (new(10f / 16f, 3f / 16f, 6f / 16f), new(1f, 13f / 16f, 10f / 16f)),
        3 => (new(0f, 3f / 16f, 6f / 16f), new(6f / 16f, 13f / 16f, 10f / 16f)),
        _ => (new(OFFSET, 0, OFFSET), new(OFFSET + SIZE, HEIGHT, OFFSET + SIZE))
    };

    // Zero-based (facing) offsets applied to smoke-particle spawn position so the smoke
    // rises from the visual tip of the torch rather than the block's center - indices
    // line up with GetWallTorchBounds' facing values (0=N,1=S,2=E,3=W).
    private static readonly Vector3[] WallParticleOffsets =
    [
        new(0f, 0f, -0.25f),   // North
        new(0f, 0f,  0.25f),   // South
        new(0.25f, 0f, 0f),    // East
        new(-0.25f, 0f, 0f)    // West
    ];

    // Torch texture is a partial tile (a narrow strip cut out of the full atlas tile at
    // column 6, row 0) rather than a whole tile, since the torch mesh is thin.
    public override TextureCoords TopTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 7, 2, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 10);
    public override TextureCoords InventoryTextureCoords => UvHelper.FromTileCoords(6, 0);

    /// <summary>
    /// Periodic purely-cosmetic tick (does not mutate world state) that spawns a smoke
    /// particle above the torch's flame. Reads this block's own facing metadata directly
    /// from the world: metadata 0 (ground torch) spawns smoke at the block center, while
    /// metadata 1-4 (wall torch facings) offsets the spawn position using
    /// <see cref="WallParticleOffsets"/>, converting the 1-based metadata to the
    /// zero-based facing index via `meta - 1`.
    /// </summary>
    public override void RandomDisplayTick(int x, int y, int z, Random random)
    {
        int meta = World.Current?.GetMetadata(x, y, z) ?? 0;
        if (meta == 0)
            Game.Instance?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z));
        else
            Game.Instance?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z) + WallParticleOffsets[meta - 1]);
    }

    // Torches cannot be supported by/attached to glass (presumably a light-blend/visual
    // restriction), even though glass is otherwise solid enough to support other blocks.
    public override bool CanBlockSupport(BlockType beneath) => beneath != BlockType.Glass;
}
