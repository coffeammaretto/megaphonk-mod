using Assets.Scripts.Settings___Saves.SaveFiles;
using MegabonkTogether.Common.Messages.GameNetworkMessages;
using MegabonkTogether.Configuration;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts.Button;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace MegabonkTogether.Scripts.Modal
{
    /// <summary>
    /// Party rules and run modifiers, opened by the host from the character selection
    /// </summary>
    internal class NetplayOptionsModal : ModalBase
    {
        private const float CHECKBOX_COLUMN_X = 245f;

        internal static readonly (RunModifier Flag, string Name, string Effect)[] ModifierList =
        [
            (RunModifier.GlassCannon, "Glass Cannon", "Damage +50%, max HP -40%"),
            (RunModifier.Horde, "Horde", "Enemies x1.5"),
            (RunModifier.ThickSkinned, "Thick Skinned", "Enemy HP x2, luck +0.25"),
            (RunModifier.Frenzy, "Frenzy", "Enemies +10% speed every 5 min, gold +20%"),
            (RunModifier.Lucky, "Lucky", "Luck +0.3, difficulty +0.3"),
            (RunModifier.Poverty, "Poverty", "Gold -50%, XP +15%"),
            (RunModifier.Colossus, "Colossus", "Enemy HP +50%, XP +25%"),
            (RunModifier.BrittleSwarm, "Brittle Swarm", "Enemy HP -40%, enemies x1.5, XP -20%"),
            (RunModifier.Bloodbath, "Bloodbath", "Enemy damage +50%, healing -50%, gold +25%"),
            (RunModifier.Pinball, "Pinball", "Knockback x2, move speed +15%"),
            (RunModifier.SilverRush, "Silver Rush", "Silver +50%, enemy damage +25%"),
            (RunModifier.Overdrive, "Overdrive", "Move speed +20%, projectile speed +25%, enemy speed +20%"),
            (RunModifier.Shockwave, "Shockwave", "Size +25%, damage -20%"),
            (RunModifier.RisingTide, "Rising Tide", "Enemies scale 50% faster, chests -25% price"),
        ];

        private static bool isOpen;

        private MainMenu mainMenu;

        public static bool IsOpen => isOpen;

        protected override Vector2 PanelSize => new Vector2(1000, 940);

        public void SetMainMenu(MainMenu menu)
        {
            mainMenu = menu;
        }

        public void Awake()
        {
            isOpen = true;
        }

        public override void OnDestroy()
        {
            isOpen = false;
            base.OnDestroy();
        }

        protected override void Update()
        {
            base.Update();

            // The character selection is gone (back, disconnect): the options with it
            if (!(WindowManager.activeWindow is CharacterMenu))
            {
                CloseModal();
            }
        }

        protected override void OnUICreated()
        {
            CreateTitle("Netplay Options", 60, new Vector2(0f, 410f), new Vector2(600, 70));
            CreatePartySettings();
            CreateTitle("Run modifiers", 36, new Vector2(0f, 20f), new Vector2(600, 50));
            CreateModifierCheckboxes();
            CreateCloseButton();
        }

        private void CreateTitle(string text, float fontSize, Vector2 position, Vector2 size)
        {
            var titleObj = new GameObject(text);
            titleObj.transform.SetParent(panel.transform, false);

            var rect = titleObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var title = titleObj.AddComponent<TextMeshProUGUI>();
            title.text = Loc.T(text);
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = fontSize;
            title.enableWordWrapping = false;
            title.color = Color.white;
        }

        /// <summary>
        /// A button cloned from the main menu's play button, with the game's own click logic replaced
        /// </summary>
        private CustomButton CreateButton(string text, float fontSize, Vector2 position, Vector2 size)
        {
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

            var localizeStringEvent = buttonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEvent != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEvent);
            }

            var customButton = buttonObj.AddComponent<CustomButton>();

            var textWrapper = buttonObj.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.text = Loc.T(text);
            textWrapper.t_text.fontSize = fontSize;

            var rect = buttonObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            customButton.state = MyButton.EButtonState.Active;
            customButton.RefreshState();

            return customButton;
        }

        private void CreateCloseButton()
        {
            var closeButton = CreateButton("Close", 36, new Vector2(0f, -420f), new Vector2(300, 60));
            closeButton.SetOnClickAction(() =>
            {
                AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
                CloseModal();
            });
        }

        /// <summary>
        /// Two columns of checkboxes, one per modifier: any number of them can be on
        /// </summary>
        private void CreateModifierCheckboxes()
        {
            for (var i = 0; i < ModifierList.Length; i++)
            {
                var (flag, name, effect) = ModifierList[i];
                var x = i % 2 == 0 ? -CHECKBOX_COLUMN_X : CHECKBOX_COLUMN_X;
                var y = -40f - (i / 2) * 52f;

                var button = CreateButton("", 24, new Vector2(x, y), new Vector2(470, 48));

                // The button's own text is centered and keeps being re-aligned: left aligned text of our own instead
                var buttonText = button.GetComponent<ButtonTextWrapper>().t_text;
                buttonText.enabled = false;
                CreateLeftText(button.gameObject, $"{Loc.T(name)}\n<size=60%>{Loc.T(effect)}</size>", buttonText);

                var check = CreateCheckbox(button.gameObject);

                void Refresh() => check.SetActive(ModConfig.RunModifiers.Value.HasFlag(flag));
                Refresh();

                button.SetOnClickAction(() =>
                {
                    AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
                    ModConfig.RunModifiers.Value ^= flag;
                    ModConfig.Save();
                    Refresh();
                });
            }
        }

        private static void CreateLeftText(GameObject parent, string text, TextMeshProUGUI template)
        {
            var textObj = new GameObject("LeftText");
            textObj.transform.SetParent(parent.transform, false);

            var rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(64f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.font = template.font;
            tmp.fontSharedMaterial = template.fontSharedMaterial;
            tmp.fontStyle = template.fontStyle;
            tmp.fontSize = template.fontSize;
            tmp.color = template.color;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            tmp.text = text;
        }

        /// <summary>
        /// The box on the left of a modifier button; returns the check mark inside it
        /// </summary>
        private static GameObject CreateCheckbox(GameObject parent)
        {
            var boxObj = new GameObject("Checkbox");
            boxObj.transform.SetParent(parent.transform, false);

            var boxRect = boxObj.AddComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0.5f, 0.5f);
            boxRect.anchoredPosition = new Vector2(34f, 0f);
            boxRect.sizeDelta = new Vector2(34f, 34f);

            var boxImage = boxObj.AddComponent<Image>();
            boxImage.color = Color.white;
            boxImage.raycastTarget = false;

            var innerObj = new GameObject("Inner");
            innerObj.transform.SetParent(boxObj.transform, false);
            var innerRect = innerObj.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(3f, 3f);
            innerRect.offsetMax = new Vector2(-3f, -3f);
            var innerImage = innerObj.AddComponent<Image>();
            innerImage.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            innerImage.raycastTarget = false;

            var checkObj = new GameObject("Check");
            checkObj.transform.SetParent(boxObj.transform, false);
            var checkRect = checkObj.AddComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(7f, 7f);
            checkRect.offsetMax = new Vector2(-7f, -7f);
            var checkImage = checkObj.AddComponent<Image>();
            checkImage.color = new Color(0.3f, 0.85f, 0.3f, 1f);
            checkImage.raycastTarget = false;

            return checkObj;
        }

        /// <summary>
        /// Party rules (host settings, apply to everyone)
        /// </summary>
        private void CreatePartySettings()
        {
            var y = 320f;

            CreateCycleSetting("Reward pause\nParty: everyone pauses, Own: no pause", new Vector2(0f, y),
                () => ModConfig.RewardPauseMode.Value.ToString(),
                step => ModConfig.RewardPauseMode.Value = ModConfig.RewardPauseMode.Value == RewardPauseMode.Party
                    ? RewardPauseMode.Own
                    : RewardPauseMode.Party);

            CreateCycleSetting("Downed state\nA teammate can revive you", new Vector2(0f, y -= 58f),
                () => ModConfig.DownedEnabled.Value ? "ON" : "OFF",
                step => ModConfig.DownedEnabled.Value = !ModConfig.DownedEnabled.Value);

            CreateCycleSetting("Team meter\nKill together for a team buff", new Vector2(0f, y -= 58f),
                () => ModConfig.TeamMeterEnabled.Value ? "ON" : "OFF",
                step => ModConfig.TeamMeterEnabled.Value = !ModConfig.TeamMeterEnabled.Value);

            CreateCycleSetting("Coop challenges\nRewarded with chests", new Vector2(0f, y -= 58f),
                () => ModConfig.ChallengesEnabled.Value ? "ON" : "OFF",
                step => ModConfig.ChallengesEnabled.Value = !ModConfig.ChallengesEnabled.Value);

            var statModes = (PartyStatMode[])System.Enum.GetValues(typeof(PartyStatMode));
            CreateCycleSetting("Party luck/difficulty\nHow players' values combine", new Vector2(0f, y -= 58f),
                () => ModConfig.PartyStatMode.Value.ToString(),
                step => ModConfig.PartyStatMode.Value = statModes[(System.Array.IndexOf(statModes, ModConfig.PartyStatMode.Value) + step + statModes.Length) % statModes.Length]);
        }

        /// <summary>
        /// A settings row (the game's enum setting: label, left/right arrows, value) bound to a config value
        /// </summary>
        private void CreateCycleSetting(string label, Vector2 position, System.Func<string> getValue, System.Action<int> step)
        {
            var settings = mainMenu.settings.GetComponent<Settings>();
            var row = GameObject.Instantiate(settings.GetSettingPrefab(SettingType.Enum), panel.transform);

            var rectTransform = row.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = new Vector2(700, 55);

            TextMeshProUGUI valueText = null;
            foreach (var textComp in Il2CppFindHelper.RuntimeGetComponentsInChildren<TextMeshProUGUI>(row))
            {
                if (textComp.name.StartsWith("Text"))
                {
                    textComp.text = Loc.T(label);
                    textComp.fontSize = 18;
                    textComp.enableWordWrapping = false;
                }
                else if (textComp.name.StartsWith("StatusText"))
                {
                    valueText = textComp;
                    valueText.gameObject.SetActive(true);
                }
            }

            void Refresh()
            {
                if (valueText != null)
                {
                    valueText.text = Loc.T(getValue());
                    valueText.color = Color.white;
                }
            }

            void OnArrow(int direction)
            {
                AudioManager.Instance.PlaySfx(AudioManager.Instance.uiClick.sounds[0]);
                step(direction);
                ModConfig.Save();
                Refresh();
            }

            foreach (var btn in Il2CppFindHelper.RuntimeGetComponentsInChildren<UnityEngine.UI.Button>(row))
            {
                if (btn.name != "B_Left" && btn.name != "B_Right")
                {
                    continue;
                }

                var origButton = btn.GetComponent<MyButtonNormal>();
                if (origButton != null)
                {
                    UnityEngine.Object.DestroyImmediate(origButton);
                }

                btn.onClick = new();
                var direction = btn.name == "B_Left" ? -1 : 1;
                btn.gameObject.AddComponent<CustomButton>().SetOnClickAction(() => OnArrow(direction));
            }

            Refresh();
        }
    }
}
