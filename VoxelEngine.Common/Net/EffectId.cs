// Beta's "world effect" ids, as sent in SoundEffect. | Stage 11

namespace VoxelEngine.Net;

/// <summary>
/// What happened somewhere in the world, for clients to turn into a sound and some particles. The
/// packet carries no volume - the server sends it to everyone in range and each client attenuates
/// for itself.
/// </summary>
public enum EffectId
{
    BlockBreak = 2001,
    Splash = 2002,
    DoorToggle = 1003,
    Extinguish = 1004,
}
