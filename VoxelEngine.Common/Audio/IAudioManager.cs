// The slice of AudioManager that shared world code actually calls. SFML.Audio can't be
// referenced from Common, so the client's AudioManager implements this and the server
// binds NullAudioManager instead. | DA

using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.Audio;

/// <summary>
/// Sound effects the world/entity/block code triggers. Deliberately narrow: it's the 45 call
/// sites that used to go through <c>Game.Instance.AudioManager</c>, nothing more. Music,
/// device setup, and cleanup stay on the concrete client-side AudioManager.
/// </summary>
public interface IAudioManager
{
    int SfxVol { get; set; }
    int MusicVol { get; set; }

    void PlayAudio(string filePath, int vol, bool loop = false, float forcePitch = -1f);
    void PlayLandingSound(BlockBreakMaterial material);
    void PlayBlockBreakSound(BlockBreakMaterial material, float volumeScale = 1f);
    void PlayBlockContactSound(BlockBreakMaterial material, int volume = -1);
    void PlayPickupSound();
    void PlayMunchSound();
    void PlayPlayerHurtSound();
}

/// <summary>
/// Silent implementation for headless hosts. A dedicated server still runs the same block and
/// mob code, which still asks for sounds; this throws them away rather than making every call
/// site null-check.
/// </summary>
public sealed class NullAudioManager : IAudioManager
{
    public int SfxVol { get; set; }
    public int MusicVol { get; set; }

    public void PlayAudio(string filePath, int vol, bool loop = false, float forcePitch = -1f) { }
    public void PlayLandingSound(BlockBreakMaterial material) { }
    public void PlayBlockBreakSound(BlockBreakMaterial material, float volumeScale = 1f) { }
    public void PlayBlockContactSound(BlockBreakMaterial material, int volume = -1) { }
    public void PlayPickupSound() { }
    public void PlayMunchSound() { }
    public void PlayPlayerHurtSound() { }
}
