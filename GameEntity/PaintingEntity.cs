// Painting entity, hangs on a wall, no physics, rendered separately by PaintingRenderer | DA | 2/27/26


using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

/// <summary>
/// A painting hung on a wall. Unlike most entities, it has no physics/gravity and never moves once placed - it just sits at a fixed anchor block/facing and periodically checks that its wall is still there. Rendering is intentionally a no-op here (<see cref="DrawModel"/>) because paintings are drawn separately by a dedicated PaintingRenderer that batches painting art textures, rather than going through the normal per-entity model draw path.
/// </summary>
public class PaintingEntity : Entity
{
    public override bool IsTargetable => true;
    public override float ShadowSize => 0f;

    // Block position the painting is anchored to (the wall block behind its bottom-left corner).
    public Vector3i AnchorPos;
    // Index into FacingVectors/WallRightVectors (0-3) - which cardinal direction the painting faces.
    public byte Facing;
    // Art/size definition (image, width/height in blocks and pixels) for this painting.
    public PaintingDef Art;

    // Counts ticks since the last wall-validity check; only re-validates periodically rather than every tick since checking every surrounding block is relatively expensive and the wall rarely changes.
    private int mTickTimer;
    private const int VALIDATE_INTERVAL = 100;

    // Direction the painting's front face points, indexed by Facing.
    public static readonly Vector3[] FacingVectors =
    {
        new(0, 0, -1), // North
        new(0, 0, 1), // South
        new(1, 0, 0), // East
        new(-1, 0, 0), // West
    };

    // Direction along the wall surface that increases the painting's local "right" (X) axis, indexed by Facing - used to walk across the painting's width when checking wall blocks.
    public static readonly Vector3[] WallRightVectors =
    {
        new(-1, 0, 0), // North
        new(1, 0, 0), // South
        new(0, 0, 1), // East
        new(0, 0, -1), // West
    };

    public PaintingEntity(Vector3i anchorPos, byte facing, PaintingDef art)
    {
        AnchorPos = anchorPos;
        Facing = facing;
        Art = art;
        Position = ComputeCenter(anchorPos, facing, art);
    }

    // Computes the painting's visual center in world space from its anchor block, facing, and size. The anchor is treated as the bottom-left wall block the painting is nailed to.
    public static Vector3 ComputeCenter(Vector3i anchorPos, byte facing, PaintingDef art)
    {
        var center = new Vector3(anchorPos.X + 0.5f, anchorPos.Y + 0.5f, anchorPos.Z + 0.5f);

        // 9/16 places the front face flush with the block surface
        center += FacingVectors[facing] * (9f / 16f);

        // Shift from the anchor corner to the geometric center of the painting's footprint.
        center += WallRightVectors[facing] * ((art.WidthBlocks - 1) * 0.5f);
        center += Vector3.UnitY * ((art.HeightBlocks - 1) * 0.5f);

        return center;
    }

    // Builds a thin AABB representing the painting's flat canvas, used both for entity targeting (aiming at it to break it) and for overlap checks against other paintings. SizeX/SizeY are in pixels (32px = 1 block), and a small inset (-0.01) avoids exact-edge intersections with neighboring paintings/blocks causing false positives.
    public Aabb GetPaintingAabb()
    {
        float halfW = Art.SizeX / 32f - 0.01f;
        float halfH = Art.SizeY / 32f - 0.01f;
        float halfD = 1f / 32f; // very thin along the depth axis - it's a flat picture, not a block

        var right = WallRightVectors[Facing];
        var forward = FacingVectors[Facing];
        var up = Vector3.UnitY;

        var extent = right * halfW + up * halfH + forward * halfD;
        return new Aabb(Position - extent, Position + extent);
    }

    // Checks that every wall block behind the painting is still solid and every block directly in front of the painting is still air (nothing built into it), and that it doesn't overlap any other painting. Used to detect when the supporting wall was mined out from under it.
    public bool IsValidSurface(World world)
    {
        var wallDir = -FacingVectors[Facing];
        var right = WallRightVectors[Facing];

        for (int tx = 0; tx < Art.WidthBlocks; tx++)
        {
            for (int ty = 0; ty < Art.HeightBlocks; ty++)
            {
                // Walk across the painting's footprint from the anchor, one wall block per painting-block.
                var wallPos = new Vector3i(
                    AnchorPos.X + (int)MathF.Round(right.X * tx),
                    AnchorPos.Y + ty,
                    AnchorPos.Z + (int)MathF.Round(right.Z * tx));

                var frontPos = new Vector3i(
                    wallPos.X - (int)wallDir.X,
                    wallPos.Y,
                    wallPos.Z - (int)wallDir.Z);

                if (!BlockRegistry.IsSolid(world.GetBlock(wallPos.X, wallPos.Y, wallPos.Z)))
                    return false;
                if (world.GetBlock(frontPos.X, frontPos.Y, frontPos.Z) != BlockType.Air)
                    return false;
            }
        }

        var myAabb = GetPaintingAabb();
        foreach (var entity in world.Entities)
        {
            if (entity is PaintingEntity other && other != this && myAabb.Intersects(other.GetPaintingAabb()))
                return false;
        }

        return true;
    }

    public override void Tick(World world)
    {
        // Paintings have no physics
        mTickTimer++;
        if (mTickTimer < VALIDATE_INTERVAL)
            return;

        mTickTimer = 0;
        if (!IsValidSurface(world))
            Drop(world);
    }

    // Any damage (e.g. player punching it) immediately knocks the painting off the wall - there's no health/durability, one hit is always enough.
    public override void TakeDamage(int amount) => Drop(Game.Instance.GetWorld);

    public override Aabb GetBoundingBox() => GetPaintingAabb();

    // Removes the painting entity and spawns a pickup-able item in its place, slightly offset out from the wall so it doesn't spawn inside the (now possibly missing) wall block.
    private void Drop(World world)
    {
        IsAlive = false;
        var spawnPos = Position + FacingVectors[Facing] * 0.6f;
        world.AddEntity(new DroppedItemEntity(spawnPos, ItemStack.FromItem(ItemType.Painting),
            Game.Instance.WorldTexture));
    }

    // Intentionally empty - paintings are rendered by a separate batched PaintingRenderer, not through the normal per-entity draw path.
    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
    }
}