namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// The special "empty space" block. This is the default value of BlockType (0) and is what
/// every chunk position starts as before terrain generation writes anything else, and what
/// a position becomes again after a block is mined/removed (World.SetBlock(..., BlockType.Air)).
/// It is non-solid, non-collidable, and fully transparent to light (LightOpacity 0), so
/// LightingEngine's flood-fill and ChunkMeshBuilder's face culling both treat it as "nothing
/// here" - neighboring solid faces adjacent to air are always meshed, and light passes through
/// unattenuated. It is intentionally hidden from the inventory since it isn't a placeable item.
/// </summary>
public class BlockAir : Block
{
    public override BlockType Type => BlockType.Air;
    public override string Name => "Air";
    // No light attenuation - air never blocks or dims light passing through it.
    public override int LightOpacity => 0;
    // NOTE: despite the name, this is about the block placed ABOVE causing suffocation, not air
    // itself suffocating anything; kept true here (see Block.SuffocatesBeneath) but has no practical
    // effect since air is never "placed" as a suffocation hazard in gameplay.
    public override bool SuffocatesBeneath => true;

    // Entities pass freely through air - no collision.
    public override bool IsSolid => false;
    // Air cannot be selected/placed from the inventory; it's the implicit "no block" state.
    public override bool ShowInInventory => false;
}
