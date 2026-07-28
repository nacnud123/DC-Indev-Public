using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// A custom novelty block specific to this project (named after the project's author/"Duncan").
/// Behaviorally simple: uses default Hardness/Solid/etc. from the Block base, plays glass-style
/// break particles/sound (BreakMaterial), is flammable (fire can consume/spread through it,
/// see BlockFire.GetEncouragement/GetCatchability which both list DuncanBlock explicitly), and
/// uses its own atlas tile texture on all faces.
/// </summary>
public class BlockDuncanBlock : Block
{
    public override BlockType Type => BlockType.DuncanBlock;
    public override string Name => "Duncan Block";
    // Cosmetic only: break particles/sound use the glass set despite this not being glass-like.
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Glass;
    // Fire can catch and spread through this block (low encouragement/catchability, similar to wood).
    public override bool IsFlamable => true;

    // Atlas tile (7,1); same texture used for all six faces.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
