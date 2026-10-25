using Assets.Scripts.MapGeneration.ProceduralTiles;
using HarmonyLib;
using Megaphonk.Services;
using Microsoft.Extensions.DependencyInjection;


namespace Megaphonk.Patches.MapGeneration
{
    [HarmonyPatch(typeof(Maze))]
    internal static class MazePatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetRequiredService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetRequiredService<IPlayerManagerService>();

        /// <summary>
        /// Use the same seed for all players in a netplay session
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Maze.Generate))]
        public static void Generate_Prefix(ref int seed)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return;
            }

            seed = playerManagerService.GetSeed();
        }
    }

}
