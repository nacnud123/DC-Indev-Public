// Holds structs for particles | DA | 2/5/26
using OpenTK.Mathematics;

namespace VoxelEngine.Particles;

public struct BlockParticle
{
    public Vector3 Pos;
    public Vector3 Vel;
    public Vector2 UvOffset;
    public Vector2 UvSize;
    public float Size;
    public float Lifetime;
    public float Gravity;
}

public struct SmokeParticle
{
    public Vector3 Pos;
    public Vector3 Vel;
    public float Size;
    public float Lifetime;
    public float MaxLifetime;
    public float Gravity;
}
