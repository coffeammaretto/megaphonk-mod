using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Megaphonk.Configuration;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Megaphonk.Helpers
{
    /// <summary>
    /// The mod's music in runs: the tracks of the music bundle play in a random order (each once before any repeats)
    /// instead of the stage music. Played on the game's music source, so the music volume setting and the game's
    /// effects (muffled in chests and pause, danger pitch) still apply. The main menu loops the bundle's menu track
    /// (MENU_TRACK), boss music stays the game's. In co-op the host picks the run tracks and the clients play the same
    /// one from the same position.
    /// The game has no audio file loader (DownloadHandlerAudioClip and AudioClip.Create are stripped): the tracks are
    /// AudioClips of an AssetBundle built with the game's Unity version (tools/music-bundle), loaded by Unity itself
    /// like the game's own music.
    /// </summary>
    internal static class CustomMusic
    {
        public const string BUNDLE_FILE = "megaphonk-music.bundle";
        // The main menu's track (tools/music-bundle: src/plugin/MenuMusic), not part of the run playlist
        public const string MENU_TRACK = "Megaphonk Menu";

        public static string BundlePath => Path.Combine(Path.GetDirectoryName(typeof(CustomMusic).Assembly.Location), BUNDLE_FILE);

        // Compile-time Unity types are the managed ones, without IL2CPP casts
        private static readonly MethodInfo TryCastClip = typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.TryCast)).MakeGenericMethod(typeof(AudioClip));

        private static readonly System.Random random = new();
        private static readonly Queue<AudioClip> playlist = new();

        // Playing our music on the game's source (cleared when the game plays something else or stops the music)
        private static bool active;
        // Playing the menu track instead of the run playlist
        private static bool menu;
        private static AudioClip currentClip;
        private static AudioClip[] clips;
        private static AudioClip menuClip;
        private static AssetBundleCreateRequest bundleRequest;
        private static AssetBundleRequest clipsRequest;
        private static bool failed;

        private static readonly Lazy<ISynchronizationService> synchronization = new(() => Plugin.Services.GetService<ISynchronizationService>());
        // The host's current track, played by a client (OnHostTrack)
        private static string hostTrack;
        private static float hostPosition;
        private static float hostTrackTime;
        private static bool hostTrackPending;

        // Client of a co-op run: plays the host's tracks instead of its own playlist
        private static bool IsClient => synchronization.Value?.IsServerMode() == false;

        /// <summary>
        /// The game started the stage music: play ours instead, once the bundle is loaded
        /// </summary>
        public static void OnStageMusic(MusicController controller)
        {
            if (!ModConfig.CustomMusic.Value || failed || controller?.musicSource == null)
            {
                return;
            }

            if (clips == null && bundleRequest == null && clipsRequest == null && !StartLoading())
            {
                return;
            }

            active = true;
            menu = false;
            // Not ours on the source anymore: a client waiting for the host's track took the game's stage music on the
            // source for "the game took its source back" and stopped following it (no run music after the menu track)
            currentClip = null;
            // A new stage starts on the next track
            if (clips != null)
            {
                StartTrack(controller);
            }
        }

        /// <summary>
        /// The game started the main menu music: loop ours instead, once the bundle is loaded
        /// </summary>
        public static void OnMenuMusic(MusicController controller)
        {
            if (!ModConfig.CustomMusic.Value || failed || controller?.musicSource == null)
            {
                return;
            }

            if (clips == null && bundleRequest == null && clipsRequest == null && !StartLoading())
            {
                return;
            }

            active = true;
            menu = true;
            currentClip = null;
            if (clips != null)
            {
                PlayMenu(controller);
            }
        }

        /// <summary>
        /// A client: the host started a track
        /// </summary>
        public static void OnHostTrack(string track, float position)
        {
            if (!ModConfig.CustomMusic.Value)
            {
                return;
            }

            hostTrack = track;
            hostPosition = position;
            hostTrackTime = Time.realtimeSinceStartup;
            hostTrackPending = true;
        }

        /// <summary>
        /// The game plays its own track (boss, menu) or stops the music: ours stops following the source
        /// </summary>
        public static void OnGameMusicChanged()
        {
            active = false;
        }

        /// <summary>
        /// Every frame (after the game's music update): next track when one ends
        /// </summary>
        public static void Tick(MusicController controller)
        {
            if (!active || controller?.musicSource == null)
            {
                return;
            }

            if (clips == null)
            {
                // Still loading: the game's music plays meanwhile
                if (ContinueLoading())
                {
                    if (menu)
                    {
                        PlayMenu(controller);
                    }
                    else
                    {
                        StartTrack(controller);
                    }
                }
                return;
            }

            var source = controller.musicSource;
            // The game took its source back for something else
            if (currentClip != null && source.clip != null && source.clip.Ptr() != currentClip.Ptr())
            {
                active = false;
                return;
            }

            if (menu)
            {
                // Looping: only restarts if the game stopped the source
                if (currentClip == null || (!source.isPlaying && source.time <= 0f))
                {
                    PlayMenu(controller);
                }
                return;
            }

            if (IsClient)
            {
                // The host's next track (a track that ended waits for it)
                if (hostTrackPending)
                {
                    StartTrack(controller);
                }
                return;
            }

            // A track that ended rewinds to 0 and stops; a paused one keeps its position
            if (currentClip == null || (!source.isPlaying && source.time <= 0f))
            {
                Play(controller, Next(), 0f);
            }
        }

        /// <summary>
        /// The next track of the playlist, or a client's the host's current one where the host is in it now
        /// </summary>
        private static void StartTrack(MusicController controller)
        {
            if (!IsClient)
            {
                Play(controller, Next(), 0f);
                return;
            }

            if (hostTrack == null)
            {
                return; // The game's music until the host's track comes
            }

            hostTrackPending = false;
            var clip = clips.FirstOrDefault(c => c.name == hostTrack);
            if (clip == null)
            {
                Plugin.Log.LogWarning($"Custom music: the host's track {hostTrack} is not in {BundlePath}");
                return;
            }

            var position = hostPosition + (Time.realtimeSinceStartup - hostTrackTime);
            if (position < clip.length)
            {
                Play(controller, clip, position);
            }
        }

        private static void PlayMenu(MusicController controller)
        {
            if (menuClip == null)
            {
                active = false; // No menu track in the bundle: the game's menu music
                return;
            }

            Play(controller, menuClip, 0f, true);
        }

        private static void Play(MusicController controller, AudioClip clip, float position, bool loop = false)
        {
            if (clip == null)
            {
                return;
            }

            currentClip = clip;
            var source = controller.musicSource;
            controller.isPlayingIntro = false; // else the game switches to its track's loop when "the intro" ends
            source.Stop();
            source.loop = loop;
            source.clip = clip;
            source.time = position;
            source.Play();
            Plugin.Log.LogInfo($"Custom music: playing {clip.name}");
            if (!menu && !IsClient)
            {
                synchronization.Value?.OnMusicTrackChanged(clip.name, position);
            }
        }

        /// <summary>
        /// Next clip of the shuffled playlist (refilled once every track was played; the last one played doesn't open
        /// the next round)
        /// </summary>
        private static AudioClip Next()
        {
            if (playlist.Count == 0)
            {
                var shuffled = clips.OrderBy(_ => random.Next()).ToList();
                if (shuffled.Count > 1 && currentClip != null && shuffled[0].Ptr() == currentClip.Ptr())
                {
                    shuffled.Add(shuffled[0]);
                    shuffled.RemoveAt(0);
                }
                foreach (var clip in shuffled)
                {
                    playlist.Enqueue(clip);
                }
            }
            return playlist.Dequeue();
        }

        private static bool StartLoading()
        {
            if (!File.Exists(BundlePath))
            {
                Plugin.Log.LogWarning($"Custom music: {BundlePath} not found");
                failed = true;
                return false;
            }

            try
            {
                bundleRequest = AssetBundle.LoadFromFileAsync(BundlePath);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Custom music: could not load {BundlePath}: {ex.Message}");
                failed = true;
                return false;
            }
        }

        /// <summary>
        /// Loads the bundle, then its clips (both asynchronous). True once the clips are ready
        /// </summary>
        private static bool ContinueLoading()
        {
            try
            {
                if (bundleRequest != null)
                {
                    if (!bundleRequest.isDone)
                    {
                        return false;
                    }

                    var bundle = bundleRequest.assetBundle;
                    bundleRequest = null;
                    if (bundle == null)
                    {
                        throw new InvalidOperationException("not an asset bundle of this Unity version");
                    }
                    clipsRequest = bundle.LoadAllAssetsAsync(Il2CppType.Of<AudioClip>());
                    return false;
                }

                if (clipsRequest == null || !clipsRequest.isDone)
                {
                    return false;
                }

                var loaded = clipsRequest.allAssets.Select(asset => (AudioClip)TryCastClip.Invoke(asset, null)).Where(clip => clip != null).ToArray();
                clipsRequest = null;
                foreach (var clip in loaded)
                {
                    clip.hideFlags = HideFlags.DontUnloadUnusedAsset; // survives the stage changes
                }

                var runClips = loaded.Where(clip => clip.name != MENU_TRACK).ToArray();
                if (runClips.Length == 0)
                {
                    throw new InvalidOperationException("no tracks in the bundle");
                }

                menuClip = loaded.FirstOrDefault(clip => clip.name == MENU_TRACK);
                clips = runClips;
                Plugin.Log.LogInfo($"Custom music: {clips.Length} track(s){(menuClip != null ? " and the menu track" : "")} in {BundlePath}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Custom music: could not load {BundlePath}: {ex.Message}");
                bundleRequest = null;
                clipsRequest = null;
                failed = true;
                active = false;
                return false;
            }
        }
    }
}
