// Main file for the tick system, defines TPS and has function to convert deltaTime into a fixed number of ticks | DA | 2/5/26
namespace VoxelEngine.Core;

public class TickSystem
{
    public const int TPS = 20;
    public const float TICK_DURATION = 1f / TPS;

    private float mAccumulator;

    // Adds each frame's delta time to accumulator, when it goes above TICK_DURATION it returns how many ticks should be processed. Capped at 10 tps
    public int Accumulate(float deltaTime)
    {
        mAccumulator += deltaTime;
        int ticks = 0;

        while (mAccumulator >= TICK_DURATION)
        {
            mAccumulator -= TICK_DURATION;
            ticks++;
        }

        return Math.Min(ticks, 10);
    }
}
