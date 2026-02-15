// The audio manager for the game. It lets you play sounds / manages what sounds should be played. | DA | 8/1/25 - 2/14/26
// Updated with more complex sound playing functions, also added ability to play background music.
using SFML.Audio;
using System;
using System.Collections.Generic;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.Audio
{
    public class AudioManager : IDisposable
    {
        private Dictionary<string, SoundBuffer> mSoundBuffers;
        private List<Sound> mActiveSounds;
        private bool mDisposed = false;

        public int SfxVol { get; set; }
        public int MusicVol { get; set; }

        private Sound? mBackgroundMusic;

        public AudioManager()
        {
            mSoundBuffers = new Dictionary<string, SoundBuffer>();
            mActiveSounds = new List<Sound>();
            Console.WriteLine("SFML Audio initialized");
        }

        public void PlayBackgroundMusic()
        {
            if (mDisposed || mBackgroundMusic != null)
                return;

            try
            {
                const string path = "Resources/Audio/Background.ogg";
                if (!mSoundBuffers.ContainsKey(path))
                    mSoundBuffers[path] = new SoundBuffer(path);

                mBackgroundMusic = new Sound(mSoundBuffers[path])
                {
                    IsLooping = true,
                    Volume = MusicVol,
                    Pitch = 1f
                };
                mBackgroundMusic.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing background music: {ex.Message}");
            }
        }

        public void UpdateMusicVolume() // Not needed yet
        {
            if (mBackgroundMusic != null)
                mBackgroundMusic.Volume = MusicVol;
        }

        public void PlayAudio(string filePath, int vol, bool loop = false) // Play audio at the file, with volume, also does slight randomization to the pitch of the audio
        {
            if (mDisposed) 
                return;

            try
            {
                CleanupFinishedSounds();

                if (!mSoundBuffers.ContainsKey(filePath))
                {
                    var buffer = new SoundBuffer(filePath);
                    mSoundBuffers[filePath] = buffer;
                }

                float pitch = loop ? 1f : 0.9f + (float)Game.Instance.GameRandom.NextDouble() * 0.2f;
                var sound = new Sound(mSoundBuffers[filePath])
                {
                    IsLooping = loop,
                    Volume = vol,
                    Pitch = pitch
                };

                mActiveSounds.Add(sound);
                sound.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing audio {filePath}: {ex.Message}");
            }
        }

        public void Stop() // Stop all active sounds
        {
            if (mDisposed) 
                return;

            if (mBackgroundMusic != null)
            {
                mBackgroundMusic.Stop();
                mBackgroundMusic.Dispose();
                mBackgroundMusic = null;
            }

            foreach (var sound in mActiveSounds)
            {
                try
                {
                    sound.Stop();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping sound: {ex.Message}");
                }
            }
        }

        // Play break, walk, dig, or place sounds for a block material.
        public void PlayBlockBreakSound(BlockBreakMaterial material)
        {
            if (mDisposed) return;

            string? path = GetBreakSoundPath(material);
            if (path != null)
                PlayAudio(path, SfxVol, false);
        }

        public void PlayBlockContactSound(BlockBreakMaterial material, int volume = -1)
        {
            if (mDisposed) return;

            if (volume == -1)
                volume = SfxVol;

            string? path = GetContactSoundPath(material);
            if (path != null)
                PlayAudio(path, volume, false);
        }

        private static string? GetBreakSoundPath(BlockBreakMaterial material)
        {
            return material switch
            {
                BlockBreakMaterial.Grass  => "Resources/Audio/GrassBreak.ogg",
                BlockBreakMaterial.Dirt   => "Resources/Audio/DirtBreak.ogg",
                BlockBreakMaterial.Stone  => "Resources/Audio/StoneBreak.ogg",
                BlockBreakMaterial.Glass  => "Resources/Audio/GlassBreak.ogg",
                BlockBreakMaterial.Wool   => "Resources/Audio/WoolBreak.ogg",
                BlockBreakMaterial.Sand   => "Resources/Audio/SandBreak.ogg",
                BlockBreakMaterial.Gravel => "Resources/Audio/GravelBreak.ogg",
                BlockBreakMaterial.Wooden => "Resources/Audio/WoodenBreak.ogg",
                _ => null
            };
        }

        private string? GetContactSoundPath(BlockBreakMaterial material)
        {
            return material switch
            {
                BlockBreakMaterial.Dirt   => RandomContactSound("Dirt", "DirtWalk", 3),
                BlockBreakMaterial.Grass  => RandomContactSound("Grass", "GrassWalk", 3),
                BlockBreakMaterial.Stone  => RandomContactSound("Stone", "StoneWalk", 3),
                BlockBreakMaterial.Glass  => RandomContactSound("Stone", "StoneWalk", 3),
                BlockBreakMaterial.Wool   => RandomContactSound("Wool", "WoolWalk", 2),
                BlockBreakMaterial.Sand   => RandomContactSound("Sand", "SandWalk", 2),
                BlockBreakMaterial.Gravel => RandomContactSound("Gravel", "GravelWalk", 3),
                BlockBreakMaterial.Wooden => RandomContactSound("Wood", "WoodWalk", 3),
                BlockBreakMaterial.Water  => RandomContactSound("Water", "Water", 3),
                _ => null
            };
        }

        // Gets a random contact sound, between the 1 and count
        private string RandomContactSound(string folder, string prefix, int count)
        {
            int index = Game.Instance.GameRandom.Next(1, count + 1);
            return $"Resources/Audio/Walking/{folder}/{prefix}{index}.ogg";
        }

        public void CleanupFinishedSounds()
        {
            if (mDisposed) return;

            for (int i = mActiveSounds.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (mActiveSounds[i].Status == SoundStatus.Stopped)
                    {
                        mActiveSounds[i].Dispose();
                        mActiveSounds.RemoveAt(i);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error cleaning up sound: {ex.Message}");
                    mActiveSounds.RemoveAt(i);
                }
            }
        }

        // Free up the memory
        public void Dispose()
        {
            if (mDisposed) 
                return;

            Console.WriteLine("Disposing AudioManager...");

            try
            {
                // Stop background music
                if (mBackgroundMusic != null)
                {
                    mBackgroundMusic.Stop();
                    mBackgroundMusic.Dispose();
                    mBackgroundMusic = null;
                }

                // Stop all sounds
                foreach (var sound in mActiveSounds)
                {
                    try
                    {
                        sound.Stop();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error stopping sound during dispose: {ex.Message}");
                    }
                }

                System.Threading.Thread.Sleep(50);

                // Dispose all sounds
                foreach (var sound in mActiveSounds)
                {
                    try
                    {
                        sound.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disposing sound: {ex.Message}");
                    }
                }
                mActiveSounds.Clear();

                // Dispose all sound buffers
                foreach (var buffer in mSoundBuffers.Values)
                {
                    try
                    {
                        buffer.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disposing sound buffer: {ex.Message}");
                    }
                }
                mSoundBuffers.Clear();

                Console.WriteLine("AudioManager disposed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during AudioManager disposal: {ex.Message}");
            }
            finally
            {
                mDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        // Backup
        ~AudioManager()
        {
            if (!mDisposed)
            {
                Console.WriteLine("AudioManager finalizer called - dispose was not called properly!");
                Dispose();
            }
        }
    }
}
