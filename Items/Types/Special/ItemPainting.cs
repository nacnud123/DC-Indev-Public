
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>
/// Places a painting entity on a wall. Unlike normal block placement, the painting's specific artwork (size) is chosen randomly from whichever registered PaintingDef variants actually fit the target wall surface, mirroring Minecraft's placement behavior.
/// </summary>
public class ItemPainting : Item
{
    public override ItemType Type => ItemType.Painting;
    public override string Name => "Painting";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(5, 3);
    public override int MaxStackSize => 64;

    /// <summary>
    /// Derives a cardinal facing from the direction placePos was offset from blockPos, then tries every registered painting size against that wall, keeping only ones that fit (IsValidSurface), and finally places a random one of the valid candidates.
    /// </summary>
    public override bool OnUse(World world, Vector3i blockPos, Vector3i? placePos)
    {
        if (!placePos.HasValue)
            return false;

        var diff = placePos.Value - blockPos;
        byte facing;
        if (diff.Z == -1)       facing = 0; // North
        else if (diff.Z == 1)   facing = 1; // South
        else if (diff.X == 1)   facing = 2; // East
        else if (diff.X == -1)  facing = 3; // West
        else return false;

        var candidates = new List<PaintingEntity>();
        foreach (var def in PaintingRegistry.All)
        {
            var candidate = new PaintingEntity(blockPos, facing, def);
            if (candidate.IsValidSurface(world))
                candidates.Add(candidate);
        }

        if (candidates.Count == 0)
            return false;

        var chosen = candidates[Game.Instance.GameRandom.Next(candidates.Count)];
        world.AddEntity(chosen);
        return true;
    }
}
