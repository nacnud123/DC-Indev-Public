
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Wall-mounted torch block. Unlike <see cref="BlockTorch"/> (which is a single block
/// class covering all facings via a metadata byte read at render/tick time), this class
/// is instantiated once *per facing direction* by <c>BlockRegistry</c>, each with its own
/// distinct <see cref="BlockType"/> and a fixed, zero-based `facing` (0=North, 1=South,
/// 2=East, 3=West) baked in at construction. This mirrors the standing torch's
/// wall-facing metadata values 1-4 (facing = metadata - 1) but bakes the orientation into
/// the block type itself instead of reading it dynamically, so bounds/particle offsets
/// here are plain switches on the constructor-provided <see cref="Facing"/> rather than a
/// per-call metadata lookup. Hidden from the creative inventory
/// (<see cref="ShowInInventory"/> = false) since it's a placement-time variant of the
/// regular torch, not something the player selects directly.
/// </summary>
public class BlockWallTorch(BlockType type, string name, int facing) : Block
{
    // Zero-based wall facing baked in at construction (0=N,1=S,2=E,3=W) - see class
    // summary for how this relates to BlockTorch's 1-4 metadata scheme.
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

    // Per-facing collision box min/max, identical values to BlockTorch.GetWallTorchBounds
    // but selected via the baked-in Facing field rather than a runtime metadata lookup.
    // Offsets a thin box out from the wall on the appropriate side/axis so the torch
    // juts out visually from the surface it's attached to.
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

    // Facing-indexed smoke-particle spawn offsets so smoke rises from the visual tip of
    // the torch instead of the block's center; indices match Facing (0=N,1=S,2=E,3=W).
    private readonly Vector3[] mParticleOffsets =
    [
        new Vector3(0f, 0f, -0.25f),
        new Vector3(0f, 0f,  0.25f),
        new Vector3(0.25f, 0f, 0f),
        new Vector3(-0.25f, 0f, 0f)
    ];

    // Same partial-tile texture strip as the standing torch (BlockTorch) - both variants
    // share one atlas tile since they're visually the same torch, just oriented differently.
    public override TextureCoords TopTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 7, 2, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromPartialTile(6, 0, 7, 0, 2, 10);
    public override TextureCoords InventoryTextureCoords => UvHelper.FromTileCoords(6, 0);

    /// <summary>
    /// Purely-cosmetic periodic tick that spawns a smoke particle offset toward this
    /// torch's fixed facing direction - simpler than BlockTorch's version since the
    /// facing is already known from the constructor rather than needing a metadata read.
    /// </summary>
    public override void RandomDisplayTick(int x, int y, int z, Random random)
    {
        GameContext.Current?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z) + mParticleOffsets[Facing]);
    }

    // Same restriction as the standing torch: cannot be attached to/supported by glass.
    public override bool CanBlockSupport(BlockType beneath) => beneath != BlockType.Glass;
}
