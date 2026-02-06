// Main camera class, it can move around and do other camera stuff | DA | 2/5/26
using OpenTK.Mathematics;

namespace VoxelEngine.GameEntity;

public class Camera
{
    public Vector3 Position;
    public float Pitch { get; private set; }
    public float Yaw { get; private set; } = -90f;
    public float Fov = 70f;
    public float AspectRatio;
    public float NearPlane = 0.1f;
    public float FarPlane = 500f;
    public float RenderDistance = 64f;

    private const float SENSITIVITY = 0.1f;

    public Camera(Vector3 position, float aspectRatio)
    {
        Position = position;
        AspectRatio = aspectRatio;
    }

    public Vector3 Front => new(
        MathF.Cos(MathHelper.DegreesToRadians(Pitch)) * MathF.Cos(MathHelper.DegreesToRadians(Yaw)),
        MathF.Sin(MathHelper.DegreesToRadians(Pitch)),
        MathF.Cos(MathHelper.DegreesToRadians(Pitch)) * MathF.Sin(MathHelper.DegreesToRadians(Yaw))
    );

    public Vector3 Right => Vector3.Cross(Front, Vector3.UnitY).Normalized();

    public Matrix4 GetViewMatrix() => Matrix4.LookAt(Position, Position + Front, Vector3.UnitY);

    public Matrix4 GetProjectionMatrix() => Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(Fov), AspectRatio, NearPlane, FarPlane);

    public void Rotate(float dx, float dy)
    {
        Yaw += dx * SENSITIVITY;
        Pitch = Math.Clamp(Pitch - dy * SENSITIVITY, -89f, 89f);
    }

    public void SetRotation(float pitch, float yaw)
    {
        Pitch = Math.Clamp(pitch, -89f, 89f);
        Yaw = yaw;
    }
}
