using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MegabonkTogether.Configuration;
using NLayer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace MegabonkTogether.Helpers
{
    /// <summary>
    /// The player's own music in runs: the mp3 files of the plugin's Music folder play in a random order (each once
    /// before any repeats) instead of the stage music. Played on the game's music source, so the music volume setting
    /// and the game's effects (muffled in chests and pause, danger pitch) still apply. Boss and menu music stay the
    /// game's. Local to each player: nothing goes over the network.
    /// The game has no audio file loader (DownloadHandlerAudioClip is stripped): the mp3 is decoded by NLayer on a
    /// background thread, then copied into an AudioClip a chunk per frame.
    /// </summary>
    internal static class CustomMusic
    {
        private const string Extension = ".mp3";
        private const int CHUNK_SECONDS = 2;

        // The game has only the IL2CPP overload (float array as Il2CppStructArray)
        private static readonly MethodInfo SetData = typeof(AudioClip).GetMethod(nameof(AudioClip.SetData), [typeof(Il2CppStructArray<float>), typeof(int)]);

        public static string Folder => Path.Combine(Path.GetDirectoryName(typeof(CustomMusic).Assembly.Location), "Music");

        private static readonly System.Random random = new();
        private static readonly Queue<string> playlist = new();

        // Playing our music on the game's source (cleared when the game plays something else or stops the music)
        private static bool active;
        private static AudioClip currentClip;
        // The next track: decoding, then copied into a clip, then waiting for the current track to end
        private static Task<Decoded> decoding;
        private static Decoded building;
        private static AudioClip buildingClip;
        private static int builtFrames;
        private static AudioClip pendingClip;
        private static string nextFile;
        private static bool failedFolder;

        private sealed class Decoded
        {
            public string Name;
            public float[] Samples;
            public int Channels;
            public int Rate;
        }

        private static bool Loading => decoding != null || building != null;

        /// <summary>
        /// The game started the stage music: play ours instead, when the folder has any
        /// </summary>
        public static void OnStageMusic(MusicController controller)
        {
            if (!ModConfig.CustomMusic.Value || controller?.musicSource == null)
            {
                return;
            }

            if (currentClip == null && pendingClip == null && !Loading && !LoadNext())
            {
                return; // No music of the player's
            }

            active = true;
            controller.musicSource.Stop();
            // A new stage starts on the next track when it's loaded already, else the current one starts over
            if (pendingClip != null)
            {
                if (currentClip != null)
                {
                    UnityEngine.Object.Destroy(currentClip);
                }
                var next = pendingClip;
                pendingClip = null;
                Play(controller, next);
            }
            else if (currentClip != null)
            {
                Play(controller, currentClip);
            }
        }

        /// <summary>
        /// The game plays its own track (boss, menu) or stops the music: ours stops following the source
        /// </summary>
        public static void OnGameMusicChanged()
        {
            active = false;
        }

        /// <summary>
        /// Every frame (after the game's music update): next track when one ends, keeps one loaded ahead
        /// </summary>
        public static void Tick(MusicController controller)
        {
            if (!active || controller?.musicSource == null)
            {
                return;
            }

            var source = controller.musicSource;
            // The game took its source back for something else
            if (currentClip != null && source.clip != null && source.clip.Ptr() != currentClip.Ptr())
            {
                active = false;
                return;
            }

            ContinueLoading();

            // A track that ended rewinds to 0 and stops; a paused one keeps its position
            var ended = currentClip == null || (!source.isPlaying && source.time <= 0f);
            if (!ended)
            {
                if (!Loading && pendingClip == null)
                {
                    LoadNext();
                }
                return;
            }

            if (pendingClip == null)
            {
                if (!Loading)
                {
                    LoadNext();
                }
                return;
            }

            var next = pendingClip;
            pendingClip = null;
            if (currentClip != null && currentClip.Ptr() != next.Ptr())
            {
                UnityEngine.Object.Destroy(currentClip);
            }
            Play(controller, next);
        }

        private static void Play(MusicController controller, AudioClip clip)
        {
            currentClip = clip;
            var source = controller.musicSource;
            controller.isPlayingIntro = false; // else the game switches to its track's loop when "the intro" ends
            source.loop = false;
            source.clip = clip;
            source.time = 0f;
            source.Play();
            Plugin.Log.LogInfo($"Custom music: playing {clip.name}");
        }

        /// <summary>
        /// Starts decoding the next file of the shuffled playlist (refilled once every track was played)
        /// </summary>
        private static bool LoadNext()
        {
            if (playlist.Count == 0 && !Refill())
            {
                return false;
            }

            nextFile = playlist.Dequeue();
            var file = nextFile;
            decoding = Task.Run(() => Decode(file));
            return true;
        }

        private static Decoded Decode(string file)
        {
            using var mp3 = new MpegFile(file);
            var chunks = new List<float[]>();
            var total = 0;
            while (true)
            {
                var chunk = new float[1 << 16];
                var read = mp3.ReadSamples(chunk, 0, chunk.Length);
                if (read <= 0)
                {
                    break;
                }
                if (read < chunk.Length)
                {
                    Array.Resize(ref chunk, read);
                }
                chunks.Add(chunk);
                total += read;
            }

            var samples = new float[total - total % mp3.Channels];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                var count = Math.Min(chunk.Length, samples.Length - offset);
                Array.Copy(chunk, 0, samples, offset, count);
                offset += count;
            }
            return new Decoded { Name = Path.GetFileNameWithoutExtension(file), Samples = samples, Channels = mp3.Channels, Rate = mp3.SampleRate };
        }

        /// <summary>
        /// Takes the decoded track once ready, then copies a chunk of it into its clip per frame (a whole track at once
        /// stalls a frame)
        /// </summary>
        private static void ContinueLoading()
        {
            try
            {
                if (decoding != null)
                {
                    if (!decoding.IsCompleted)
                    {
                        return;
                    }

                    var task = decoding;
                    decoding = null;
                    if (task.IsFaulted)
                    {
                        Plugin.Log.LogWarning($"Custom music: could not load {nextFile}: {task.Exception?.GetBaseException().Message}");
                        return;
                    }

                    var decoded = task.Result;
                    var frames = decoded.Samples.Length / decoded.Channels;
                    if (frames == 0)
                    {
                        Plugin.Log.LogWarning($"Custom music: {nextFile} has no audio");
                        return;
                    }

                    building = decoded;
                    builtFrames = 0;
                    buildingClip = AudioClip.Create(decoded.Name, frames, decoded.Channels, decoded.Rate, false);
                    buildingClip.name = decoded.Name;
                    buildingClip.hideFlags = HideFlags.DontUnloadUnusedAsset; // survives the stage changes
                    return;
                }

                if (building == null)
                {
                    return;
                }

                var totalFrames = building.Samples.Length / building.Channels;
                var count = Math.Min(building.Rate * CHUNK_SECONDS, totalFrames - builtFrames);
                var chunk = new Il2CppStructArray<float>(count * building.Channels);
                var start = builtFrames * building.Channels;
                for (var i = 0; i < chunk.Length; i++)
                {
                    chunk[i] = building.Samples[start + i];
                }
                SetData.Invoke(buildingClip, [chunk, builtFrames]);
                builtFrames += count;

                if (builtFrames >= totalFrames)
                {
                    pendingClip = buildingClip;
                    buildingClip = null;
                    building = null;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Custom music: could not load {nextFile}: {ex.GetBaseException().Message}");
                if (buildingClip != null)
                {
                    UnityEngine.Object.Destroy(buildingClip);
                }
                buildingClip = null;
                building = null;
            }
        }

        /// <summary>
        /// The auto-updater extracts every file of the release next to the mod's dll (it drops the folders): the bundled
        /// tracks land there instead of in the Music folder
        /// </summary>
        private static void MoveStrayTracks()
        {
            var pluginFolder = Path.GetDirectoryName(Folder);
            foreach (var file in Directory.GetFiles(pluginFolder, "*" + Extension))
            {
                try
                {
                    File.Move(file, Path.Combine(Folder, Path.GetFileName(file)), true);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Custom music: could not move {file} to {Folder}: {ex.Message}");
                }
            }
        }

        private static bool Refill()
        {
            if (failedFolder)
            {
                return false;
            }

            string[] files;
            try
            {
                Directory.CreateDirectory(Folder);
                MoveStrayTracks();
                files = Directory.GetFiles(Folder, "*" + Extension);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Custom music: could not read {Folder}: {ex.Message}");
                failedFolder = true;
                return false;
            }

            if (files.Length == 0)
            {
                return false;
            }

            // Each track once in a random order; the last one played doesn't open the next round
            var last = nextFile;
            var shuffled = files.OrderBy(_ => random.Next()).ToList();
            if (shuffled.Count > 1 && shuffled[0] == last)
            {
                shuffled.Add(shuffled[0]);
                shuffled.RemoveAt(0);
            }
            foreach (var file in shuffled)
            {
                playlist.Enqueue(file);
            }
            Plugin.Log.LogInfo($"Custom music: {files.Length} track(s) in {Folder}");
            return true;
        }
    }
}
