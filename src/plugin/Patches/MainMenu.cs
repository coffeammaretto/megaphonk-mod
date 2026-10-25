using HarmonyLib;
using MegabonkTogether.Common;
using MegabonkTogether.Configuration;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts.Button;
using MegabonkTogether.Scripts.Modal;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(MainMenu))]
    internal static class MainMenuPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetRequiredService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetRequiredService<IPlayerManagerService>();
        private static readonly IAutoUpdaterService autoUpdaterService = Plugin.Services.GetRequiredService<IAutoUpdaterService>();
        private static readonly IChangelogService changelogService = Plugin.Services.GetRequiredService<IChangelogService>();

        private static bool hasCheckedChangelog = false;

        /// <summary>
        /// Add "MEGAPHONK!" button to main menu
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(MainMenu.Start))]
        public static void Start_Postfix(MainMenu __instance)
        {
            var playBtn = __instance.btnPlay;

            var go = UnityEngine.Object.Instantiate(playBtn.gameObject);

            var originalButton = go.GetComponent<MyButtonNormal>();

            UnityEngine.UI.Button button = go.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick = new();
            }

            var customButton = go.AddComponent<PlayTogetherButton>();
            customButton.SetMainMenu(__instance);

            // AddComponent does not carry the serialized references over: without them StartHover/StopHover throw
            // (null background), which aborts ButtonManager mid-switch and leaves the menu selection stuck on this button
            if (originalButton != null)
            {
                customButton.background = originalButton.background;
                customButton.defaultColor = originalButton.defaultColor;
                customButton.hoverColor = originalButton.hoverColor;
                customButton.colorInited = originalButton.colorInited;
                customButton.scaleOnHover = originalButton.scaleOnHover;
                customButton.hoverScale = originalButton.hoverScale;
                customButton.button = originalButton.button;
                customButton.disabledOverlay = originalButton.disabledOverlay;
                customButton.customSfx = originalButton.customSfx;

                UnityEngine.Object.DestroyImmediate(originalButton);
            }

            var textWrapper = go.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.text = "MEGAPHONK!";

            CreateModVersionText(__instance);

            go.transform.SetParent(playBtn.transform.parent);

            Plugin.Instance.PlayTogetherButton = customButton;
            Plugin.Instance.SetMainMenu(__instance);

            Scripts.CoopLeaderboardPanel.Create(__instance);

            if (autoUpdaterService.IsCustomBuild() && autoUpdaterService.IsAnUpdateAvailable())
            {
                customButton.gameObject.SetActive(false);
            }

            if (!hasCheckedChangelog)
            {
                hasCheckedChangelog = true;

                // Steam is up by the main menu: fetch the interfaces once, logs the account
                if (ModConfig.UseSteamNetworking.Value)
                {
                    Steam.SteamApi.Initialize();
                }

                if (string.IsNullOrEmpty(ModConfig.PreviousVersion.Value) && !autoUpdaterService.IsCustomBuild())
                {
                    Plugin.Log.LogInfo("First time with changelog system detected");
                    ModConfig.ShowChangelog.Value = true;
                    ModConfig.PreviousVersion.Value = "1.0.0";
                    ModConfig.Save();
                }

                bool shouldShow = changelogService.ShouldShowChangelog();

                if (shouldShow)
                {
                    ShowChangelogModal();
                }
            }
        }

        private static void ShowChangelogModal()
        {
            Task.Run(async () =>
            {
                try
                {
                    var previousVersion = Configuration.ModConfig.PreviousVersion.Value;
                    var currentVersion = MyPluginInfo.PLUGIN_VERSION;

                    Plugin.Log.LogInfo($"Fetching changelog for versions: {previousVersion} -> {currentVersion}");

                    var changelog = await changelogService.LoadChangelogAsync();
                    if (changelog == null || changelog.Count == 0)
                    {
                        Plugin.Log.LogInfo("No changelog data available");
                        changelogService.MarkChangelogAsShown();
                        return;
                    }

                    var changes = changelogService.GetChangesBetweenVersions(changelog, previousVersion, currentVersion);
                    if (changes == null || changes.Count == 0)
                    {
                        Plugin.Log.LogInfo("No changes found between versions");
                        changelogService.MarkChangelogAsShown();
                        return;
                    }

                    Plugin.Log.LogInfo($"Found {changes.Count} version(s) with changes to display");

                    ChangelogModal.Show(changes);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError($"Error showing changelog: {ex.Message}");
                    changelogService.MarkChangelogAsShown();
                }
            });
        }

        /// <summary>
        /// Synchronize the selected character and prevent going to map selection if not host (Wait for host)
        /// </summary>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MainMenu.GoToMapSelection))]
        public static bool GoToMapSelection_Prefix()
        {
            if (!Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue || !Plugin.Instance.NetworkHandler.HasFoundMatch.Value)
            {
                return true;
            }

            if (synchronizationService.IsLoading())
            {
                return false;
            }

            synchronizationService.OnSelectedCharacter();

            var isHost = synchronizationService.IsServerMode() ?? false;
            if (!isHost)
            {
                Plugin.Instance.ShowModal("Waiting for the host...");
                return false;
            }

            return true;
        }

        /// <summary>
        /// The mod's version in the bottom right corner, in the line above the game's own version
        /// </summary>
        private static void CreateModVersionText(MainMenu menu)
        {
            CoroutineRunner.Instance.Run(CreateModVersionTextDelayed(menu));
        }

        // The game fills its version text a moment after the menu starts
        private static System.Collections.IEnumerator CreateModVersionTextDelayed(MainMenu menu)
        {
            yield return new UnityEngine.WaitForSeconds(1f);

            if (menu == null)
            {
                yield break;
            }

            TMPro.TextMeshProUGUI gameVersion = null;
            foreach (var candidate in menu.transform.root.gameObject.RuntimeGetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if (candidate.text != null && candidate.name != "ModVersionText" && candidate.text.Contains(UnityEngine.Application.version))
                {
                    gameVersion = candidate;
                    break;
                }
            }

            if (gameVersion == null)
            {
                Plugin.Log.LogWarning("Game version text not found, mod version not shown");
                yield break;
            }

            var versionObj = new UnityEngine.GameObject("ModVersionText");
            versionObj.transform.SetParent(gameVersion.transform.parent, false);

            var source = gameVersion.rectTransform;
            var rect = versionObj.AddComponent<UnityEngine.RectTransform>();
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot;
            rect.sizeDelta = source.sizeDelta;
            rect.anchoredPosition = source.anchoredPosition + new UnityEngine.Vector2(0f, gameVersion.fontSize * 1.1f);

            var text = versionObj.AddComponent<TMPro.TextMeshProUGUI>();
            text.font = gameVersion.font;
            text.fontSize = gameVersion.fontSize;
            text.alignment = gameVersion.alignment;
            text.color = gameVersion.color;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.text = $"Megaphonk Mod v{MyPluginInfo.PLUGIN_VERSION}";
        }

        /// <summary>
        /// Restore "MEGAPHONK!" button text when going back to main menu (because its disappears for some reason)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(MainMenu.GoToMenu))]
        public static void GoToMenu_Postfix()
        {
            if (Plugin.Instance.PlayTogetherButton != null)
            {
                var textWrapper = Plugin.Instance.PlayTogetherButton.GetComponent<ButtonTextWrapper>();
                textWrapper.t_text.text = "MEGAPHONK!";
            }
        }
    }
}
