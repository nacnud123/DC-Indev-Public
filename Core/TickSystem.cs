// Main file for the tick system, defines TPS and has function to convert deltaTime into a fixed number of ticks | DA | 2/5/26

namespace VoxelEngine.Core;

/// <summary>
/// Fixed-timestep accumulator that decouples game logic (ticks) from the variable-length render frame. The render loop calls <see cref="Accumulate"/> once per frame with the frame's delta time (in seconds); this class converts that into a whole number of 1/20th-of-a-second simulation ticks to run, buffering any leftover fractional time for the next frame. This is the same fixed-tick pattern Minecraft uses (20 TPS) so that world simulation (physics, redstone-equivalents, mob AI, etc.) behaves identically regardless of the rendering framerate.
/// </summary>
public class TickSystem
{
    // Simulation runs at a fixed 20 ticks per second, matching vanilla Minecraft's tick rate.
    public const int TPS = 20;
    // Duration of a single tick in seconds (0.05s at 20 TPS).
    public const float TICK_DURATION = 1f / TPS;

    // Leftover (fractional) time in seconds that hasn't yet accumulated into a full tick. Carried over between frames so time is never lost or double-counted.
    private float mAccumulator;

    /// <summary>
    /// Adds this frame's delta time (in seconds) to the running accumulator and drains whole ticks out of it. Returns the number of simulation ticks the caller should process this frame. The result is capped at 10 ticks/frame as a safety valve: if the game stalls (e.g. a debugger breakpoint, disk I/O hitch, or the window losing focus) for a long time, this prevents a "spiral of death" where the simulation tries to catch up with hundreds of ticks in one frame and never recovers.
    /// </summary>
    public int Accumulate(float deltaTime)
    {
        mAccumulator += deltaTime;
        int ticks = 0;

        // Drain the accumulator in fixed-size steps rather than doing one variable-size step, so tick logic always sees a constant TICK_DURATION regardless of framerate.
        while (mAccumulator >= TICK_DURATION)
        {
            mAccumulator -= TICK_DURATION;
            ticks++;
        }

        return Math.Min(ticks, 10);
    }

    /// <summary>
    /// Returns how far (in [0,1)) we are between the last completed tick and the next one. Used for interpolating rendered positions/animations smoothly between fixed ticks instead of visually snapping entities on each tick boundary.
    /// </summary>
    public float GetPartialTick() => mAccumulator / TICK_DURATION;
}