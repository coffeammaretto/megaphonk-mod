using Megaphonk.Configuration;
using Megaphonk.Helpers;
using Megaphonk.Scripts.Button;
using System;
using TMPro;
using UnityEngine;

namespace Megaphonk.Scripts.Modal
{
    /// <summary>
    /// Asks once (ModConfig.ShareCoopRuns is Ask) whether the player's co-op runs may go to the online leaderboard, saying
    /// what is sent. Nothing is sent before a Yes.
    /// </summary>
    internal class LeaderboardConsentModal : ModalBase
    {
        private CustomButton yesButton;
        private CustomButton noButton;

        protected override Vector2 PanelSize => new(760, 440);

        private static bool shown;

        /// <summary>
        /// Once a session, while not answered (after the changelog, which opens first)
        /// </summary>
        public static void ShowIfNeeded()
        {
            if (shown || ModConfig.ShareCoopRuns.Value != LeaderboardConsent.Ask)
            {
                return;
            }

            shown = true;
            var go = new GameObject("LeaderboardConsentModal");
            go.AddComponent<LeaderboardConsentModal>();
        }

        protected override void OnUICreated()
        {
            foreach (var button in WindowManager.activeWindow.allButtons)
            {
                button.SetInteractable(false);
            }

            var textObj = new GameObject("ConsentText");
            textObj.transform.SetParent(panel.transform, false);
            var rectTransform = textObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.62f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.62f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(700, 300);

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = Loc.T("Megaphonk co-op leaderboard") + "\n\n<size=70%>" + Loc.T("Share your finished co-op runs with the online leaderboard? It receives your Steam name and Steam ID (proved with a Steam session ticket), your character and kills, and the run's map and tier. A run is listed only when every player of it agreed. You can change this later in the mod's config (ShareCoopRuns).") + "</size>";
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.fontSize = 32;
            text.color = Color.white;

            yesButton = CreateButton("Share", 0.3f, () => Answer(LeaderboardConsent.Yes));
            noButton = CreateButton("Don't share", 0.7f, () => Answer(LeaderboardConsent.No));
        }

        protected void Update()
        {
            yesButton?.SetInteractable(true);
            noButton?.SetInteractable(true);
        }

        private CustomButton CreateButton(string label, float x, Action onClick)
        {
            var mainMenu = Plugin.Instance.GetMainMenu();
            if (mainMenu == null)
            {
                Plugin.Log.LogError("MainMenu not found for the leaderboard consent buttons!");
                return null;
            }

            var buttonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            buttonObj.transform.SetParent(panel.transform, false);

            var originalButton = buttonObj.GetComponent<MyButtonNormal>();
            if (originalButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalButton);
            }

            var button = buttonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick = new();
            }

            var customButton = buttonObj.AddComponent<CustomButton>();
            customButton.SetOnClickAction(onClick);

            var textWrapper = buttonObj.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.text = Loc.T(label);
            textWrapper.t_text.fontSize = 30;

            var rectTransform = buttonObj.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(x, 0.13f);
            rectTransform.anchorMax = new Vector2(x, 0.13f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(260, 50);
            return customButton;
        }

        private void Answer(LeaderboardConsent consent)
        {
            ModConfig.ShareCoopRuns.Value = consent;
            ModConfig.Save();
            Plugin.Log.LogInfo($"Co-op leaderboard sharing: {consent}");
            AudioManager.Instance?.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
            CloseModal();

            foreach (var button in WindowManager.activeWindow.allButtons)
            {
                button.SetInteractable(true);
            }
        }
    }
}
