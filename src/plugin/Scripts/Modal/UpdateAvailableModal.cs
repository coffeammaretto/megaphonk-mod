using Megaphonk.Common;
using Megaphonk.Scripts.Button;
using Microsoft.Extensions.DependencyInjection;
using TMPro;
using UnityEngine;
using Megaphonk.Helpers;

namespace Megaphonk.Scripts.Modal
{
    internal class UpdateAvailableModal : ModalBase
    {
        private CustomButton closeButton;
        private readonly IAutoUpdaterService autoUpdaterService = Plugin.Services.GetService<IAutoUpdaterService>();

        protected override Vector2 PanelSize => new(500, 300);

        protected override void OnUICreated()
        {
            foreach (var button in WindowManager.activeWindow.allButtons)
            {
                button.SetInteractable(false);
            }

            CreateVersionText();
            CreateCloseButton();
        }

        protected void Update()
        {
            if (closeButton != null)
            {
                closeButton.SetInteractable(true);
            }
        }

        private void CreateVersionText()
        {
            var textObj = new GameObject("VersionText");
            textObj.transform.SetParent(panel.transform, false);

            var rectTransform = textObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.6f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.6f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(450, 120);

            var text = textObj.AddComponent<TextMeshProUGUI>();
            if (autoUpdaterService.IsModManagerInstall())
            {
                text.text = Loc.F("Megaphonk Mod version {0}\nis available!\nUpdate it in your mod manager.", autoUpdaterService.GetDownloadedVersion());
            }
            else
            {
                text.text = Loc.F("Megaphonk Mod version {0}\nis available!\nDownload it from GitHub.", autoUpdaterService.GetDownloadedVersion());
            }
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 30;
            text.color = Color.white;
        }

        private void CreateCloseButton()
        {
            var mainMenu = Plugin.Instance.GetMainMenu();
            if (mainMenu == null)
            {
                Plugin.Log.LogError("MainMenu not found for close button!");
                return;
            }

            var buttonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            buttonObj.transform.SetParent(panel.transform, false);

            var originalButton = buttonObj.GetComponent<MyButtonNormal>();
            if (originalButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalButton);
            }

            UnityEngine.UI.Button button = buttonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick = new();
            }

            closeButton = buttonObj.AddComponent<CustomButton>();
            closeButton.SetOnClickAction(OnCloseClicked);

            var textWrapper = buttonObj.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.text = Loc.T("Close");
            textWrapper.t_text.fontSize = 32;

            var rectTransform = buttonObj.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.15f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.15f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, 0);
            rectTransform.sizeDelta = new Vector2(200, 50);
        }

        private void OnCloseClicked()
        {
            AudioManager.Instance?.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
            CloseModal();

            foreach (var button in WindowManager.activeWindow.allButtons)
            {
                button.SetInteractable(true);
            }
        }
    }
}
