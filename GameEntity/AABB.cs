// AABB for collision | DA | 2/5/26
using OpenTK.Mathematics;

namespace VoxelEngine.GameEntity;

public struct Aabb
{
    public Vector3 Min, Max;

    public Aabb(Vector3 min, Vector3 max) { Min = min; Max = max; }

    public bool Intersects(Aabb other) =>
        Min.X < other.Max.X && Max.X > other.Min.X &&
        Min.Y < other.Max.Y && Max.Y > other.Min.Y &&
        Min.Z < other.Max.Z && Max.Z > other.Min.Z;

    public Aabb Offset(Vector3 v) => new(Min + v, Max + v);

    public Aabb Expand(Vector3 v)
    {
        var min = Min; var max = Max;
        if (v.X < 0) 
            min.X += v.X; 
        else 
            max.X += v.X;
        
        if (v.Y < 0) 
            min.Y += v.Y; 
        else 
            max.Y += v.Y;
        
        if (v.Z < 0) 
            min.Z += v.Z; 
        else 
            max.Z += v.Z;
        
        return new Aabb(min, max);
    }

    public static Aabb BlockAabb(int x, int y, int z) => new(new Vector3(x, y, z), new Vector3(x + 1, y + 1, z + 1));
}
