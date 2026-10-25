using MegabonkTogether.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace MegabonkTogether.Helpers
{
    /// <summary>
    /// The player's own music in runs: the mp3/ogg/wav files of the plugin's Music folder play in a random order (each
    /// once before any repeats) instead of the stage music. Played on the game's music source, so the music volume
    /// setting and the game's effects (muffled in chests and pause, danger pitch) still apply. Boss and menu music stay
    /// the game's. Local to each player: nothing goes over the network.
    /// </summary>
    internal static class CustomMusic
    {
        private static readonly Dictionary<string, AudioType> Formats = new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = AudioType.MPEG,
            [".ogg"] = AudioType.OGGVORBIS,
            [".wav"] = AudioType.WAV,
        };

        public static string Folder => Path.Combine(Path.GetDirectoryName(typeof(CustomMusic).Assembly.Location), "Music");

        private static readonly System.Random random = new();
        private static readonly Queue<string> playlist = new();

        // Playing our music on the game's source (cleared when the game plays something else or stops the music)
        private static bool active;
        private static AudioClip currentClip;
        // The next track, loaded ahead
        private static UnityWebRequest nextRequest;
        private static string nextFile;
        private static bool failedFolder;

        /// <summary>
        /// The game started the stage music: play ours instead, when the folder has any
        /// </summary>
        public static void OnStageMusic(MusicController controller)
        {
            if (!ModConfig.CustomMusic.Value || controller?.musicSource == null)
            {
                return;
            }

            if (currentClip == null && nextRequest == null && !LoadNext())
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

            AudioClip loaded = null;
            if (nextRequest != null && nextRequest.isDone)
            {
                loaded = FinishLoading();
            }

            // A track that ended rewinds to 0 and stops; a paused one keeps its position
            var ended = currentClip == null || (!source.isPlaying && source.time <= 0f);
            if (!ended)
            {
                if (nextRequest == null && loaded == null && pendingClip == null)
                {
                    LoadNext();
                }
                pendingClip ??= loaded;
                return;
            }

            var next = pendingClip ?? loaded;
            if (next == null)
            {
                if (nextRequest == null)
                {
                    LoadNext();
                }
                return;
            }

            pendingClip = null;
            if (currentClip != null && currentClip.Ptr() != next.Ptr())
            {
                UnityEngine.Object.Destroy(currentClip);
            }
            Play(controller, next);
        }

        // Loaded ahead, waiting for the current track to end
        private static AudioClip pendingClip;

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
        /// Starts loading the next file of the shuffled playlist (refilled once every track was played)
        /// </summary>
        private static bool LoadNext()
        {
            if (playlist.Count == 0 && !Refill())
            {
                return false;
            }

            nextFile = playlist.Dequeue();
            try
            {
                nextRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(nextFile).AbsoluteUri, Formats[Path.GetExtension(nextFile)]);
                // Kept compressed in memory (an mp3 takes its file size, not minutes of raw samples)
                nextRequest.downloadHandler.Cast<DownloadHandlerAudioClip>().compressed = true;
                nextRequest.SendWebRequest();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Custom music: could not load {nextFile}: {ex.Message}");
                nextRequest = null;
                return false;
            }
        }

        private static AudioClip FinishLoading()
        {
            var request = nextRequest;
            nextRequest = null;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogWarning($"Custom music: could not load {nextFile}: {request.error}");
                    return null;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null || clip.length <= 0f)
                {
                    Plugin.Log.LogWarning($"Custom music: {nextFile} has no audio");
                    return null;
                }

                clip.name = Path.GetFileNameWithoutExtension(nextFile);
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset; // survives the stage changes
                return clip;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Custom music: could not load {nextFile}: {ex.Message}");
                return null;
            }
            finally
            {
                request.Dispose();
            }
        }

        /// <summary>
        /// The auto-updater extracts every file of the release next to the mod's dll (it drops the folders): the bundled
        /// tracks land there instead of in the Music folder
        /// </summary>
        private static void MoveStrayTracks()
        {
            var pluginFolder = Path.GetDirectoryName(Folder);
            foreach (var file in Directory.GetFiles(pluginFolder).Where(f => Formats.ContainsKey(Path.GetExtension(f))))
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
                files = Directory.GetFiles(Folder).Where(f => Formats.ContainsKey(Path.GetExtension(f))).ToArray();
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
