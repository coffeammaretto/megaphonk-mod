using Assets.Scripts.Steam;
using HarmonyLib;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Megaphonk.Patches
{
    [HarmonyPatch(typeof(Leaderboards))]
    internal static class LeaderBoardsPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly ICoopLeaderboardService coopLeaderboardService = Plugin.Services.GetService<ICoopLeaderboardService>();

        /// <summary>
        /// Prevents the original leaderboard score upload when running netplay ssession: the host submits the run to the
        /// co-op leaderboard instead (the game uploads at the end of the run, death or win)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Leaderboards.UploadScore))]
        public static bool UploadScore_Prefix()
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            Plugin.Log.LogInfo("Blocking leaderboard upload");
            coopLeaderboardService.SubmitRun();
            return false;
        }
    }
}
