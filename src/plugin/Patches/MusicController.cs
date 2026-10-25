using HarmonyLib;
using MegabonkTogether.Helpers;

namespace MegabonkTogether.Patches
{
    /// <summary>
    /// The player's own music in runs (see CustomMusic)
    /// </summary>
    [HarmonyPatch(typeof(MusicController))]
    internal static class MusicControllerPatches
    {
        /// <summary>
        /// A stage starts its music
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(MusicController.OnStageStarted))]
        public static void OnStageStarted_Postfix(MusicController __instance)
        {
            CustomMusic.OnStageMusic(__instance);
        }

        /// <summary>
        /// The stage music again, after the final boss cinematic
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(MusicController.PlayStageMusic))]
        public static void PlayStageMusic_Postfix(MusicController __instance)
        {
            CustomMusic.OnStageMusic(__instance);
        }

        /// <summary>
        /// The game plays one of its tracks (boss, menu, and the stage one just before PlayStageMusic_Postfix)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MusicController.PlayMusicTrack))]
        public static void PlayMusicTrack_Prefix()
        {
            CustomMusic.OnGameMusicChanged();
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(MusicController.StopMusic))]
        public static void StopMusic_Prefix()
        {
            CustomMusic.OnGameMusicChanged();
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(MusicController.Update))]
        public static void Update_Postfix(MusicController __instance)
        {
            CustomMusic.Tick(__instance);
        }
    }
}
