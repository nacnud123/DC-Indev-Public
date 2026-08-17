// Main camera class, it can move around and do other camera stuff | DA | 2/5/26


using VoxelEngine.Core;

namespace VoxelEngine.GameEntity;

/// <summary>
/// First-person camera: owns the player's eye position and look direction (yaw/pitch), and builds
/// the view/projection matrices used every frame to render the world. Y-up, right-handed
/// coordinate system consistent with the rest of the engine (System.Numerics conventions).
/// Yaw/Pitch are stored in degrees; trig helpers convert to radians as needed.
///
/// View bobbing and the sprint FOV kick live here too, but deliberately only affect the *matrices*.
/// <see cref="Position"/> stays the true eye point, because block and entity raycasting reads it -
/// baking the bob into it would make the crosshair wander as you walked.
/// </summary>
public class Camera
{
    public Vector3 Position;
    // Pitch: look up/down, clamped to [-89, 89] degrees to avoid gimbal flip at the poles.
    public float Pitch { get; private set; }
    // Yaw: look left/right in degrees. Starts at -90 so Front initially points down -Z (matches
    // the world's forward axis at spawn) instead of +X, which is what 0 degrees would give.
    public float Yaw { get; private set; } = -90f;

    /// <summary>The player's configured field of view, before the sprint kick is applied.</summary>
    public float BaseFov = 70f;

    /// <summary>The FOV actually rendered with this frame, eased toward the sprint target.</summary>
    public float Fov { get; private set; } = 70f;

    public float AspectRatio;
    public float NearPlane = 0.1f;
    public float RenderDistance = 64f;

    // Degrees of yaw/pitch rotation applied per unit of raw mouse delta.
    private const float SENSITIVITY = 0.1f;

    // --- View bobbing (b1.7.3 EntityRenderer.setupViewBobbing) ------------------------------
    //
    // mBobAmount is vanilla's cameraYaw: a smoothed measure of how fast the player is moving along
    // the ground, capped at 0.1, that scales the whole effect. mFallTilt is cameraPitch, the
    // slight nose-down lean while falling. Both ease toward their target rather than snapping,
    // which is what stops the bob from starting and stopping abruptly.
    private const float BOB_MAX_AMOUNT = 0.1f;
    private const float BOB_EASE_PER_TICK = 0.4f;     // cameraYaw += (target - cameraYaw) * 0.4
    private const float FALL_TILT_EASE_PER_TICK = 0.8f;
    private const float BOB_SWAY_SCALE = 0.5f;        // horizontal sway, in blocks per unit of amount
    private const float BOB_ROLL_DEGREES = 3.0f;      // camera roll into each step
    private const float BOB_PITCH_DEGREES = 5.0f;     // nod on each footfall

    private float mBobAmount;
    private float mFallTilt;
    private float mWalkDistance; // interpolated distance walked, drives the bob phase

    /// <summary>Set false to render without view bobbing (a common accessibility option).</summary>
    public bool ViewBobbing = true;

    // --- Sprint FOV -------------------------------------------------------------------------
    // Not b1.7.3 (it arrived with sprinting in 1.8), but it's the whole reason sprinting reads as
    // speed rather than just as a bigger number.
    private const float SPRINT_FOV_MULT = 1.15f;
    private const float FOV_EASE_PER_SECOND = 8f;

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

    /// <summary>
    /// Advances the view-bob state. <paramref name="walkDistance"/> is the player's accumulated
    /// distance walked (already interpolated by the partial tick), which drives the bob's phase -
    /// tying it to distance rather than time is what keeps the bob locked to the footstep sounds
    /// at any speed.
    /// </summary>
    public void UpdateBob(float walkDistance, float horizontalSpeed, bool onGround, float deltaTime)
    {
        mWalkDistance = walkDistance;

        // Vanilla's target is the per-tick horizontal displacement, capped at 0.1, and zeroed
        // whenever the player isn't on the ground.
        float target = onGround ? MathF.Min(horizontalSpeed / TickSystem.TPS, BOB_MAX_AMOUNT) : 0f;

        // The vanilla eases are per-tick constants; raising them to (dt * TPS) makes the same
        // approach rate hold at any framerate.
        float bobEase = 1f - MathF.Pow(1f - BOB_EASE_PER_TICK, deltaTime * TickSystem.TPS);
        float tiltEase = 1f - MathF.Pow(1f - FALL_TILT_EASE_PER_TICK, deltaTime * TickSystem.TPS);

        mBobAmount += (target - mBobAmount) * bobEase;
        // Falling leans the view down slightly; on the ground it returns to level.
        mFallTilt += ((onGround ? 0f : 2f) - mFallTilt) * tiltEase;
    }

    /// <summary>Eases the rendered FOV toward its sprinting or resting target.</summary>
    public void UpdateFov(bool sprinting, float deltaTime)
    {
        float target = sprinting ? BaseFov * SPRINT_FOV_MULT : BaseFov;
        Fov += (target - Fov) * MathF.Min(1f, FOV_EASE_PER_SECOND * deltaTime);
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

    /// <summary>
    /// View matrix: world space into camera space, with the view bob applied afterwards in camera
    /// space (which is why it multiplies on the right - the look transform has to happen first).
    /// </summary>
    public Matrix4x4 GetViewMatrix()
    {
        var look = Matrix4x4.CreateLookAt(Position, Position + Front, Vector3.UnitY);

        if (!ViewBobbing || mBobAmount <= 0.0001f)
            return look;

        // Phase advances with distance walked; the negation and the PI scaling put one full bob
        // cycle at two steps, so the camera dips once per footfall.
        float phase = -mWalkDistance * MathF.PI;
        float sway = MathF.Sin(phase) * mBobAmount * BOB_SWAY_SCALE;
        // Always downward: the head drops on each footfall and returns, it never rises above rest.
        float drop = -MathF.Abs(MathF.Cos(phase) * mBobAmount);

        float roll = float.DegreesToRadians(MathF.Sin(phase) * mBobAmount * BOB_ROLL_DEGREES);
        float nod = float.DegreesToRadians(MathF.Abs(MathF.Cos(phase - 0.2f) * mBobAmount) * BOB_PITCH_DEGREES
                                           + mFallTilt);

        // Camera-space transform order is the reverse of the classic GL call order
        // (translate, roll, nod) because these are row-vector matrices.
        var bob = Matrix4x4.CreateRotationX(nod)
                  * Matrix4x4.CreateRotationZ(roll)
                  * Matrix4x4.CreateTranslation(sway, drop, 0f);

        return look * bob;
    }

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
