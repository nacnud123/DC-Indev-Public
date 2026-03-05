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

    private float mShakeTimer;
    private float mShakeDuration;
    private const float SHAKE_INTENSITY = 5f; // degrees

    public Camera(Vector3 position, float aspectRatio)
    {
        Position = position;
        AspectRatio = aspectRatio;
    }

    public void Shake(float duration)
    {
        mShakeTimer    = duration;
        mShakeDuration = duration;
    }

    public void UpdateShake(float deltaTime)
    {
        if (mShakeTimer > 0f)
            mShakeTimer -= deltaTime;
    }

    private float ShakeYawOffset()
    {
        if (mShakeTimer <= 0f || mShakeDuration <= 0f) return 0f;
        // t goes 1→0 over the duration; sine gives a kick-left then return arc
        float t = mShakeTimer / mShakeDuration;
        return SHAKE_INTENSITY * MathF.Sin(t * MathF.PI);
    }

    public Vector3 Front
    {
        get
        {
            float yaw   = MathHelper.DegreesToRadians(Yaw + ShakeYawOffset());
            float pitch = MathHelper.DegreesToRadians(Pitch);
            return new(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw));
        }
    }

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
