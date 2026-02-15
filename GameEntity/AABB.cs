// AABB for collision. Axis-Aligned Bounding Box | DA | 2/5/26
using OpenTK.Mathematics;

namespace VoxelEngine.GameEntity;

public struct Aabb
{
    public Vector3 Min, Max;

    public Aabb(Vector3 min, Vector3 max) { Min = min; Max = max; }

    // 3-Axis overlap text, returns true if all three axis ranges overlap
    public bool Intersects(Aabb other) =>
        Min.X < other.Max.X && Max.X > other.Min.X &&
        Min.Y < other.Max.Y && Max.Y > other.Min.Y &&
        Min.Z < other.Max.Z && Max.Z > other.Min.Z;

    // Returns a new AABB shifted by vector v
    public Aabb Offset(Vector3 v) => new(Min + v, Max + v);

    // Extends the AABB in a direction, used to create a swept volume that covers where the entity will move
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
