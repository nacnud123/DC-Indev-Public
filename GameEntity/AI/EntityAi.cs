// Parent AI class holds stuff for path calculation and other movement code | DA | 2/5/26
using OpenTK.Mathematics;
using VoxelEngine.GameEntity.AI.Pathfinding;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

public abstract class EntityAi
{
    protected readonly Entity ParentEntity;
    protected readonly Random Random = new();
    private readonly AStarPathfinder mPathfinder = new();

    protected Stack<Vector3i>? CurrentPath;
    protected Vector3i? CurrentTarget;
    protected int StateTimer;
    protected int PathRecalculateTimer;

    protected bool WasHurt;

    public virtual bool IsFleeing => false;

    protected EntityAi(Entity entity)
    {
        ParentEntity = entity;
    }
    
    public abstract void Tick(World world);

    protected void RecalculatePath(World world)
    {
        if (!CurrentTarget.HasValue)
            return;

        Vector3i startPos = new(
            (int)MathF.Floor(ParentEntity.Position.X),
            (int)MathF.Floor(ParentEntity.Position.Y),
            (int)MathF.Floor(ParentEntity.Position.Z)
        );

        CurrentPath = mPathfinder.FindPath(world, startPos, CurrentTarget.Value);
    }

    protected bool ReachedTarget()
    {
        if (!CurrentTarget.HasValue)
            return true;

        float distX = MathF.Abs(ParentEntity.Position.X - (CurrentTarget.Value.X + 0.5f));
        float distZ = MathF.Abs(ParentEntity.Position.Z - (CurrentTarget.Value.Z + 0.5f));

        return distX < 0.25f && distZ < 0.25f;
    }

    protected int FindGroundLevel(World world, float x, float z)
    {
        int startY = (int)MathF.Floor(ParentEntity.Position.Y);
        int xI = (int)MathF.Floor(x);
        int zI = (int)MathF.Floor(z);

        // Downward
        for (int y = startY; y > 0; y--)
        {
            if (world.GetBlock(xI, y - 1, zI) != BlockType.Air && world.GetBlock(xI, y, zI) == BlockType.Air && world.GetBlock(xI, y + 1, zI) == BlockType.Air)
            {
                return y;
            }
        }

        // Upward
        for (int y = startY + 1; y < startY + 5; y++)
        {
            if (world.GetBlock(xI, y - 1, zI) != BlockType.Air && world.GetBlock(xI, y, zI) == BlockType.Air && world.GetBlock(xI, y + 1, zI) == BlockType.Air)
            {
                return y;
            }
        }

        return -1;
    }

    protected bool ShouldJump(World world, float dirX, float dirZ)
    {
        int checkX = (int)MathF.Floor(ParentEntity.Position.X + dirX * 1.0f);
        int checkY = (int)MathF.Floor(ParentEntity.Position.Y);
        int checkZ = (int)MathF.Floor(ParentEntity.Position.Z + dirZ * 1.0f);

        bool blockAtFeet = world.GetBlock(checkX, checkY, checkZ) != BlockType.Air;
        bool blockAtHead = world.GetBlock(checkX, checkY + 1, checkZ) != BlockType.Air;

        return blockAtFeet && !blockAtHead;
    }

    // Move body so it faces the movement direction
    protected void FaceMovementDirection()
    {
        if (MathF.Abs(ParentEntity.Velocity.X) > 0.01f || MathF.Abs(ParentEntity.Velocity.Z) > 0.01f)
            ParentEntity.Yaw = MathF.Atan2(ParentEntity.Velocity.X, ParentEntity.Velocity.Z) - MathF.PI / 2;
    }

    public void OnHurt()
    {
        WasHurt = true;
    }
}
