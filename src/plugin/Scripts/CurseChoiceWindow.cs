using Megaphonk.Common.Messages.GameNetworkMessages;
using Megaphonk.Helpers;
using Megaphonk.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Megaphonk.Scripts
{
    /// <summary>
    /// Curse or pact choice (CurseService): three cards over the game, picked with 1/2/3 or a click.
    /// Plain objects driven by CurseService.Tick, nothing injected in Il2Cpp.
    /// </summary>
    internal class CurseChoiceWindow
    {
        private static readonly Color DIM_COLOR = new(0f, 0f, 0f, 0.7f);
        private static readonly Color CARD_COLOR = new(0.1f, 0.06f, 0.12f, 0.97f);
        private static readonly Color CARD_HOVER_COLOR = new(0.2f, 0.1f, 0.22f, 0.97f);
        private static readonly Color TEXT_COLOR = new(0.95f, 0.92f, 0.98f, 1f);
        private static readonly Vector2 CARD_SIZE = new(380f, 340f);
        private static readonly Color BOON_COLOR = new(0.4f, 0.95f, 0.55f);
        private const float CARD_SPACING = 420f;
        private const float BORDER = 4f;

        private static readonly BepInEx.Unity.IL2CPP.UnityEngine.KeyCode[][] CardKeys =
        [
            [BepInEx.Unity.IL2CPP.UnityEngine.KeyCode.Alpha1, BepInEx.Unity.IL2CPP.UnityEngine.KeyCode.Keypad1],
            [BepInEx.Unity.IL2CPP.UnityEngine.KeyCode.Alpha2, BepInEx.Unity.IL2CPP.UnityEngine.KeyCode.Keypad2],
            [BepInEx.Unity.IL2CPP.UnityEngine.KeyCode.Alpha3, BepInEx.Unity.IL2CPP.UnityEngine.KeyCode.Keypad3],
        ];

        private readonly GameObject root;
        private readonly List<(RectTransform Rect, Image Background)> cards = [];
        private readonly Action<int> onChosen;
        private readonly bool[] wasKeyDown = new bool[3];
        private readonly Camera camera;
        private readonly bool previousCursorVisible;
        private readonly CursorLockMode previousCursorLock;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private bool isClosed;

        public static CurseChoiceWindow Open(string title, string subtitle, IReadOnlyList<ChoiceOffer> offers, IReadOnlyList<ChoiceOffer> taken, Action<int> onChosen)
        {
            var parent = UiManager.Instance?.transform;
            if (parent == null)
            {
                return null;
            }

            try
            {
                return new CurseChoiceWindow(parent, title, subtitle, offers, taken, onChosen);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not open the curse choice: {ex}");
                return null;
            }
        }

        private CurseChoiceWindow(Transform parent, string title, string subtitle, IReadOnlyList<ChoiceOffer> offers, IReadOnlyList<ChoiceOffer> taken, Action<int> onChosen)
        {
            this.onChosen = onChosen;

            // The game's font (the default one has no Cyrillic)
            var gameText = Il2CppFindHelper.RuntimeGetComponentsInChildren<TextMeshProUGUI>(parent, true).FirstOrDefault(t => t.font != null);
            font = gameText?.font;
            fontMaterial = gameText?.fontSharedMaterial;

            root = AddRect("Megaphonk_CurseChoice", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            try
            {
                Build(title, subtitle, offers, taken);
            }
            catch
            {
                UnityEngine.Object.Destroy(root); //Nothing half built left on screen
                throw;
            }

            var canvas = GetRootCanvas(root.transform);
            camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;

            // Keys already held when the window opens don't pick
            for (var i = 0; i < wasKeyDown.Length; i++)
            {
                wasKeyDown[i] = CardKeys[i].Any(key => BepInEx.Unity.IL2CPP.UnityEngine.Input.GetKeyInt(key));
            }
        }

        private void Build(string titleText, string subtitle, IReadOnlyList<ChoiceOffer> offers, IReadOnlyList<ChoiceOffer> taken)
        {
            root.transform.SetAsLastSibling();
            var dim = root.AddComponent<Image>();
            dim.color = DIM_COLOR;
            dim.raycastTarget = true; //The game's UI below can't be clicked

            var title = AddText("Title", root.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 230f), new Vector2(0f, 330f), 54f, TextAlignmentOptions.Center);
            title.text = $"{titleText}\n<size=60%>{subtitle}</size>";

            for (var i = 0; i < offers.Count; i++)
            {
                AddCard(root.transform, i, offers[i], (i - (offers.Count - 1) / 2f) * CARD_SPACING);
            }

            var footer = AddText("Footer", root.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(40f, -360f), new Vector2(-40f, -200f), 26f, TextAlignmentOptions.Top);
            footer.enableWordWrapping = true;
            footer.text = $"{Loc.T("Press 1, 2 or 3, or click a card")}\n\n<color=#B9A5C8>{Loc.T("Your curses")}: {(taken.Count == 0 ? Loc.T("none") : string.Join(", ", taken.Select(o => o.Boon == null ? Loc.T(o.Curse.Curse.Name) : $"{Loc.T(o.Boon.Name)} + {Loc.T(o.Curse.Curse.Name)}")))}</color>";
        }

        /// <summary>
        /// "RRGGBB" for rich text (ColorUtility.ToHtmlStringRGB is stripped from the game)
        /// </summary>
        private static string ToHex(Color color)
        {
            static int Channel(float value) => Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
            return $"{Channel(color.r):X2}{Channel(color.g):X2}{Channel(color.b):X2}";
        }

        private void AddCard(Transform parent, int index, ChoiceOffer choice, float x)
        {
            var offer = choice.Curse;
            var tierColor = offer.Tier switch
            {
                CurseTier.Lesser => new Color(0.95f, 0.85f, 0.4f),
                CurseTier.Greater => new Color(0.95f, 0.25f, 0.25f),
                _ => new Color(0.95f, 0.55f, 0.2f)
            };

            var border = AddRect($"Card{index}", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            border.sizeDelta = CARD_SIZE;
            border.anchoredPosition = new Vector2(x, 20f);
            border.gameObject.AddComponent<Image>().color = choice.Boon == null ? tierColor : BOON_COLOR;

            var card = AddRect("Background", border, Vector2.zero, Vector2.one, new Vector2(BORDER, BORDER), new Vector2(-BORDER, -BORDER));
            var background = card.gameObject.AddComponent<Image>();
            background.color = CARD_COLOR;

            var tierName = offer.Tier switch
            {
                CurseTier.Lesser => "Lesser",
                CurseTier.Greater => "Greater",
                _ => "Normal"
            };

            var header = AddText("Name", card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -100f), new Vector2(-16f, -14f), 34f, TextAlignmentOptions.Top);
            header.enableWordWrapping = true;
            header.text = choice.Boon == null
                ? $"<color=#{ToHex(tierColor)}>[{index + 1}]</color> {Loc.T(offer.Curse.Name)}\n<size=65%><color=#{ToHex(tierColor)}>{Loc.T(tierName)}</color></size>"
                : $"<color=#{ToHex(BOON_COLOR)}>[{index + 1}]</color> {Loc.T(choice.Boon.Name)}\n<size=65%><color=#{ToHex(tierColor)}>+ {Loc.T(offer.Curse.Name)} ({Loc.T(tierName)})</color></size>";

            // The boon, then the curse: what helps in green, what costs in red (a higher chest price is a cost)
            var lines = (choice.Boon?.Effects ?? []).Select(effect => FormatEffect(effect, 1f))
                .Concat(FormatPerk(choice.Boon?.Perk ?? Perk.None, 1f))
                .Concat(offer.Curse.Effects.Select(effect => FormatEffect(effect, offer.Scale)))
                .Concat(FormatPerk(offer.Curse.Perk, offer.Scale))
                .ToList();
            var effects = AddText("Effects", card, Vector2.zero, new Vector2(1f, 1f), new Vector2(20f, 16f), new Vector2(-20f, -120f), lines.Count > 5 ? 22f : 28f, TextAlignmentOptions.Top);
            effects.enableWordWrapping = true;
            effects.text = string.Join("\n", lines);

            cards.Add((border, background));
        }

        private static IEnumerable<string> FormatPerk(Perk perk, float strength)
        {
            if (perk != Perk.None)
            {
                yield return $"<color=#{(PerkService.IsGood(perk) ? "77FF99" : "FF7777")}>{PerkService.DescribePerk(perk, strength)}</color>";
            }
        }

        private static string FormatEffect(CurseEffect effect, float scale)
        {
            var color = effect.IsGood ? "77FF99" : "FF7777";
            var value = effect.IsFlat ? Mathf.RoundToInt(effect.Scaled(scale)) : Mathf.RoundToInt(effect.Scaled(scale) * 100f);
            return $"{Loc.T(effect.Label)} <color=#{color}>{(value > 0 ? "+" : "-")}{Math.Abs(value)}{(effect.IsFlat ? string.Empty : "%")}</color>";
        }

        /// <summary>
        /// Every frame while open (CurseService.Tick)
        /// </summary>
        public void Update()
        {
            if (isClosed)
            {
                return;
            }

            // The game may lock it again (it's paused anyway)
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            for (var i = 0; i < cards.Count; i++)
            {
                var isKeyDown = CardKeys[i].Any(key => BepInEx.Unity.IL2CPP.UnityEngine.Input.GetKeyInt(key));
                var isPressed = isKeyDown && !wasKeyDown[i];
                wasKeyDown[i] = isKeyDown;
                if (isPressed)
                {
                    Choose(i);
                    return;
                }
            }

            var mouse = Rewired.ReInput.controllers?.Mouse;
            if (mouse == null)
            {
                return;
            }

            var position = mouse.screenPosition;
            var hovered = -1;
            for (var i = 0; i < cards.Count; i++)
            {
                var isHovered = RectTransformUtility.RectangleContainsScreenPoint(cards[i].Rect, position, camera);
                cards[i].Background.color = isHovered ? CARD_HOVER_COLOR : CARD_COLOR;
                if (isHovered)
                {
                    hovered = i;
                }
            }

            if (hovered >= 0 && mouse.GetButtonDown(0))
            {
                Choose(hovered);
            }
        }

        private void Choose(int index)
        {
            AudioManager.Instance?.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
            onChosen(index);
        }

        public void Destroy()
        {
            if (isClosed)
            {
                return;
            }

            isClosed = true;
            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLock;
            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
            }
        }

        /// <summary>
        /// The outermost canvas above (GetComponentInParent is stripped from the game)
        /// </summary>
        private static Canvas GetRootCanvas(Transform transform)
        {
            Canvas root = null;
            for (var parent = transform; parent != null; parent = parent.parent)
            {
                var canvas = parent.GetComponent<Canvas>();
                if (canvas != null)
                {
                    root = canvas;
                }
            }
            return root;
        }

        private static RectTransform AddRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private TextMeshProUGUI AddText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, float fontSize, TextAlignmentOptions alignment)
        {
            var text = AddRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
                text.fontSharedMaterial = fontMaterial;
            }
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = TEXT_COLOR;
            text.richText = true;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }
    }
}
