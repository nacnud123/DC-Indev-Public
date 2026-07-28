// Main camera class, it can move around and do other camera stuff | DA | 2/5/26


namespace VoxelEngine.GameEntity;

/// <summary>
/// First-person camera: owns the player's eye position and look direction (yaw/pitch), and builds
/// the view/projection matrices used every frame to render the world. Y-up, right-handed
/// coordinate system consistent with the rest of the engine (System.Numerics conventions).
/// Yaw/Pitch are stored in degrees; trig helpers convert to radians as needed.
/// </summary>
public class Camera
{
    public Vector3 Position;
    // Pitch: look up/down, clamped to [-89, 89] degrees to avoid gimbal flip at the poles.
    public float Pitch { get; private set; }
    // Yaw: look left/right in degrees. Starts at -90 so Front initially points down -Z (matches
    // the world's forward axis at spawn) instead of +X, which is what 0 degrees would give.
    public float Yaw { get; private set; } = -90f;
    public float Fov = 70f;
    public float AspectRatio;
    public float NearPlane = 0.1f;
    public float RenderDistance = 64f;

    // Degrees of yaw/pitch rotation applied per unit of raw mouse delta.
    private const float SENSITIVITY = 0.1f;

    // Screen-shake state (e.g. on taking damage): mShakeTimer counts down from mShakeDuration each
    // frame; while > 0 the camera's yaw gets a temporary sinusoidal kick (see ShakeYawOffset).
    private float mShakeTimer;
    private float mShakeDuration;
    private const float SHAKE_INTENSITY = 5f; // degrees

    public Camera(Vector3 position, float aspectRatio)
    {
        Position = position;
        AspectRatio = aspectRatio;
    }

    // Starts (or restarts) a shake effect that decays over `duration` seconds.
    public void Shake(float duration)
    {
        mShakeTimer    = duration;
        mShakeDuration = duration;
    }

    // Ticks down the shake timer; deltaTime is in seconds (frame time, not fixed tick time).
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

    // Unit vector the camera is looking along, derived from yaw/pitch via standard spherical-to-
    // Cartesian conversion (yaw rotates around Y, pitch tilts up/down). Recomputed on every access
    // rather than cached, and includes the shake offset so rendering always sees the shaken look.
    public Vector3 Front
    {
        get
        {
            float yaw   = float.DegreesToRadians(Yaw + ShakeYawOffset());
            float pitch = float.DegreesToRadians(Pitch);
            return new(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw));
        }
    }

    // Camera-local right vector (perpendicular to Front and world-up), used for strafing.
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));

    // View matrix: transforms world-space coordinates into camera-space, looking from Position
    // toward Position + Front with world-up as the up vector (right-handed, Y-up).
    public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(Position, Position + Front, Vector3.UnitY);

    // Projection matrix: perspective projection using vertical FOV (degrees, converted to radians).
    // The far clip plane is derived from RenderDistance (which is player-adjustable at runtime,
    // see Game.cs) rather than a fixed constant: a perspective depth buffer's precision is
    // spread non-linearly across the whole near..far range, so a far plane far beyond whatever is
    // actually ever drawn (chunks are culled at RenderDistance) wastes most of that precision and
    // causes z-fighting on nearby/overlapping geometry (e.g. leaves, cave walls) instead of where
    // it's actually needed. 1.5x covers the diagonal of a square render-distance cutoff; the 150f
    // floor keeps the sky dome/celestial objects (~100 units out, see SkyRenderer) from clipping
    // even at the minimum render distance.
    public Matrix4x4 GetProjectionMatrix()
    {
        float far = Math.Max(RenderDistance * 1.5f, 150f);
        return Matrix4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(Fov), AspectRatio, NearPlane, far);
    }

    // Applies raw mouse-delta input to yaw/pitch. dx/dy are unscaled pixel deltas from the input
    // system; SENSITIVITY converts them into degrees. Pitch is clamped to avoid flipping over
    // the top/bottom of the view (gimbal lock at the poles). dy is subtracted because screen-space
    // Y grows downward while pitch should increase when the mouse moves up.
    public void Rotate(float dx, float dy)
    {
        Yaw += dx * SENSITIVITY;
        Pitch = Math.Clamp(Pitch - dy * SENSITIVITY, -89f, 89f);
    }

    // Directly sets orientation (e.g. when restoring a saved camera state), bypassing sensitivity scaling.
    public void SetRotation(float pitch, float yaw)
    {
        Pitch = Math.Clamp(pitch, -89f, 89f);
        Yaw = yaw;
    }
}
