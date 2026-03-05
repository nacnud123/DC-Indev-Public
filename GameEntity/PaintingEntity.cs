// Painting entity, hangs on a wall, no physics, rendered separately by PaintingRenderer | DA | 2/27/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

public class PaintingEntity : Entity
{
    public override bool IsTargetable => true;

    public Vector3i AnchorPos;
    public byte Facing;
    public PaintingDef Art;

    private int mTickTimer;
    private const int VALIDATE_INTERVAL = 100;

    public static readonly Vector3[] FacingVectors =
    {
        new(0, 0, -1), // North
        new(0, 0, 1), // South
        new(1, 0, 0), // East
        new(-1, 0, 0), // West
    };

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

    public static Vector3 ComputeCenter(Vector3i anchorPos, byte facing, PaintingDef art)
    {
        var center = new Vector3(anchorPos.X + 0.5f, anchorPos.Y + 0.5f, anchorPos.Z + 0.5f);

        // 9/16 places the front face flush with the block surface
        center += FacingVectors[facing] * (9f / 16f);

        center += WallRightVectors[facing] * ((art.WidthBlocks - 1) * 0.5f);
        center += Vector3.UnitY * ((art.HeightBlocks - 1) * 0.5f);

        return center;
    }

    public Aabb GetPaintingAabb()
    {
        float halfW = Art.SizeX / 32f - 0.01f;
        float halfH = Art.SizeY / 32f - 0.01f;
        float halfD = 1f / 32f;

        var right = WallRightVectors[Facing];
        var forward = FacingVectors[Facing];
        var up = Vector3.UnitY;

        var extent = right * halfW + up * halfH + forward * halfD;
        return new Aabb(Position - extent, Position + extent);
    }

    public bool IsValidSurface(World world)
    {
        var wallDir = -FacingVectors[Facing];
        var right = WallRightVectors[Facing];

        for (int tx = 0; tx < Art.WidthBlocks; tx++)
        {
            for (int ty = 0; ty < Art.HeightBlocks; ty++)
            {
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

    public override void TakeDamage(int amount) => Drop(Game.Instance.GetWorld);

    public override Aabb GetBoundingBox() => GetPaintingAabb();

    private void Drop(World world)
    {
        IsAlive = false;
        var spawnPos = Position + FacingVectors[Facing] * 0.6f;
        world.AddEntity(new DroppedItemEntity(spawnPos, ItemStack.FromItem(ItemType.Painting),
            Game.Instance.WorldTexture));
    }

    protected override void DrawModel(Matrix4 view, Matrix4 projection)
    {
    }
}