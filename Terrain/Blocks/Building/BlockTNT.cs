
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

// The TNT block itself. It doesn't explode while sitting in the world - OnRemoved is called
// whenever the block is destroyed (mined, burned, or caught in another explosion), and that's
// what actually spawns the ticking TntEntity via Block.Explode().
public class BlockTNT : Block
{
    public override BlockType Type => BlockType.TNT;
    public override string Name => "TNT";
    public override float Hardness => 0.0f; // Breaks instantly (matches vanilla TNT's near-zero hardness).
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Grass;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6, 5);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(6, 5);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(5, 5);

    // TNT doesn't drop an item when destroyed - it just explodes.
    public override ItemStack? GetDrop(byte metadata) => null;

    // Set by ExplosionUtil just before clearing a TNT block caught in a blast, so the resulting
    // TntEntity uses a near-instant fuse instead of the normal placed-by-hand fuse.
    public static float? PendingChainFuse;

    // Whenever a TNT block is destroyed by any means, it always ignites (spawns the ticking
    // TntEntity via Block.Explode) - there's no way to "silently" remove TNT once placed.
    public override void OnRemoved(World world, int x, int y, int z)
    {
        if (Game.Instance == null)
            return;

        // Use a near-instant chain-reaction fuse if one was set by ExplosionUtil (this TNT was caught
        // in another blast); otherwise fall back to the normal hand-ignited fuse duration.
        float fuse = PendingChainFuse ?? 4.0f;
        PendingChainFuse = null;

        this.Explode(world, x, y, z, fuse);
    }
}
