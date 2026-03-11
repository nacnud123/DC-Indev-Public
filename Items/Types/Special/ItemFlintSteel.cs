using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemFlintSteel : Item
{
    public override ItemType Type => ItemType.FlintSteel;
    public override string Name => "Flint & Steel";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(5, 7);
    public override ToolType ToolType => ToolType.Misc;
    public override int MaxDurability => 64;
    public override int MaxStackSize => 1;

    public override bool OnUse(World world, Vector3i blockPos, Vector3i? placePos)
    {
        if (!placePos.HasValue)
            return false;

        int fx = placePos.Value.X, fy = placePos.Value.Y, fz = placePos.Value.Z;
        if (world.GetBlock(fx, fy, fz) != BlockType.Air)
            return false;

        world.SetBlock(fx, fy, fz, BlockType.Fire);
        world.SetChunkAsModified(fx, fy, fz);
        return true;
    }
}
