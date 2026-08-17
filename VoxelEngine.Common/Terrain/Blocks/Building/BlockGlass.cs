using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Transparent building block. Mostly opaque to face-culling logic aside (low LightOpacity
/// lets light through and keeps adjacent glass/opaque faces from being culled against it), and it
/// drops nothing when broken (shatters instead of yielding an item, like vanilla Minecraft glass).</summary>
public class BlockGlass : Block
{
    public override BlockType Type => BlockType.Glass;
    public override string Name => "Glass";
    public override float Hardness => 0.2f;
    // Shatters when broken - no item drop.
    public override ItemStack? GetDrop(byte metadata) => null;
    public override int LightOpacity => 2; // Nearly fully transparent to light.
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Glass;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}

