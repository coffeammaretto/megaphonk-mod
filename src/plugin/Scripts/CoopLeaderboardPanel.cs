using Il2CppInterop.Runtime.Attributes;
using MegabonkTogether.Common.Messages.GameNetworkMessages;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MegabonkTogether.Scripts
{
    /// <summary>
    /// Co-op leaderboard (see CoopLeaderboardService) in the main menu, above the game's leaderboard and in its style (its
    /// window visuals and entry prefab are cloned). A page shows 5 runs: party characters, run modifiers, party kills.
    /// Clicking a run shows the party's Steam names under the mouse.
    /// </summary>
    internal class CoopLeaderboardPanel : MonoBehaviour
    {
        private const int PAGE_SIZE = 5;
        private const float PANEL_HEIGHT = 310f;
        private const float HEADER_HEIGHT = 52f;
        private const float FILTERS_HEIGHT = 36f;
        private const float ROW_GAP = 4f;
        private const float PADDING = 8f;
        // The game's entry prefab is laid out for this size, it is scaled down to the panel's width
        private static readonly Vector2 ENTRY_SIZE = new(840f, 60f);
        private const float TEXT_SCALE = 1.7f;

        private static readonly Color TEXT_COLOR = new(1f, 0.93f, 0.8f);
        private static readonly Color SELECTED_COLOR = new(1f, 0.84f, 0f);
        private static readonly Color DIM_COLOR = new(0.65f, 0.6f, 0.55f);

        private readonly ICoopLeaderboardService coopLeaderboardService = Plugin.Services.GetService<ICoopLeaderboardService>();

        private class Row
        {
            public GameObject Root;
            public RectTransform Rect;
            public TextMeshProUGUI Rank;
            public TextMeshProUGUI Score;
            public GameObject Hover;
            public MaskableGraphic Outline;
            public Color ColorDefault;
            public Color ColorGold;
            public RectTransform Icons;
            public CoopLeaderboardEntry Entry;
        }

        private GameObject entryPrefab;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private RectTransform panelRect;
        private RectTransform rowsRect;
        private TextMeshProUGUI pageText;
        private TextMeshProUGUI prevText;
        private TextMeshProUGUI nextText;
        private TextMeshProUGUI globalText;
        private TextMeshProUGUI friendsText;
        private TextMeshProUGUI mapText;
        private TextMeshProUGUI tierText;
        private TextMeshProUGUI sizeText;
        private TextMeshProUGUI statusText;
        private readonly List<Row> rows = [];
        private GameObject tooltip;
        private TextMeshProUGUI tooltipText;
        private Row tooltipRow;

        private int mapIndex;
        private int tier = 1;
        private int partySize = CoopLeaderboardService.MIN_PARTY;
        private bool friendsOnly;
        private int page;
        private int total;

        // Filled by the request's thread, shown on the main thread
        private readonly object resultLock = new();
        private int requestId;
        private CoopLeaderboardPage pendingPage;
        private string pendingError;

        private static readonly Dictionary<RunModifier, Texture2D> modifierIcons = [];

        /// <summary>
        /// Adds the panel above the game's leaderboard of this main menu
        /// </summary>
        [HideFromIl2Cpp]
        public static void Create(MainMenu mainMenu)
        {
            try
            {
                var widget = mainMenu?.leaderboards;
                var gameLeaderboard = widget?.GetComponentInChildren<LeaderboardUiNew>(true);
                if (widget == null || gameLeaderboard?.lbPrefab == null)
                {
                    Plugin.Log.LogWarning("Co-op leaderboard: the game's leaderboard was not found in the main menu");
                    return;
                }

                var go = new GameObject("CoopLeaderboard");
                go.AddComponent<RectTransform>();
                go.transform.SetParent(widget.transform, false);
                go.AddComponent<CoopLeaderboardPanel>().Build(widget, gameLeaderboard.lbPrefab);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not create the co-op leaderboard: {ex}");
            }
        }

        [HideFromIl2Cpp]
        private void Build(GameObject widget, GameObject prefab)
        {
            entryPrefab = prefab;

            var title = widget.transform.Find("Header/Header/Content/Title/T_Title")?.GetComponent<TextMeshProUGUI>();
            font = title?.font;
            fontMaterial = title?.fontSharedMaterial;

            // On top of the game's window (above its "Leaderboards" button when it shows)
            panelRect = GetComponent<RectTransform>();
            var button = widget.transform.Find("B_Leaderboards")?.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(widget.GetComponent<RectTransform>().sizeDelta.x, PANEL_HEIGHT);
            panelRect.anchoredPosition = new Vector2(0f, (button != null && button.gameObject.activeSelf ? button.anchoredPosition.y : 0f) + 12f);

            // The game window's look
            AddVisual(widget, "Shadow", stretch: false);
            if (AddVisual(widget, "WindowLayers/Content/ContentMask/ContentLayout/Visuals/Visual_Content", stretch: true) == null)
            {
                var background = AddRect("Background", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
                background.color = new Color(0.12f, 0.1f, 0.14f, 0.95f);
            }
            var header = AddVisual(widget, "Header/Header/Background", stretch: true);
            if (header != null)
            {
                SetRect(header, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -HEADER_HEIGHT), Vector2.zero);
            }
            var headerBorder = AddVisual(widget, "Header/Header/Border", stretch: true);
            if (headerBorder != null)
            {
                SetRect(headerBorder, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -HEADER_HEIGHT), Vector2.zero);
            }
            AddVisual(widget, "Header/Border", stretch: true);

            // Header: title, page and its arrows
            var titleText = AddText("Title", transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -HEADER_HEIGHT), new Vector2(-120f, 0f), 30f, TextAlignmentOptions.MidlineLeft);
            titleText.text = Loc.T("Co-op Leaderboard");
            prevText = AddText("Prev", transform, Vector2.one, Vector2.one, new Vector2(-118f, -HEADER_HEIGHT), new Vector2(-88f, 0f), 30f, TextAlignmentOptions.Center);
            prevText.text = "<";
            pageText = AddText("Page", transform, Vector2.one, Vector2.one, new Vector2(-88f, -HEADER_HEIGHT), new Vector2(-38f, 0f), 24f, TextAlignmentOptions.Center);
            nextText = AddText("Next", transform, Vector2.one, Vector2.one, new Vector2(-38f, -HEADER_HEIGHT), new Vector2(-8f, 0f), 30f, TextAlignmentOptions.Center);
            nextText.text = ">";

            // Filters, the game's tabs style: global / friends, map, tier, party size
            var filtersTop = -HEADER_HEIGHT - 2f;
            var width = panelRect.sizeDelta.x;
            float[] columns = [0f, 0.2f, 0.42f, 0.64f, 0.8f, 1f];
            // A lambda, not a local function: that one would be a method of this class taking its captures by reference,
            // which the IL2CPP class injection can't register
            Func<string, int, TextMeshProUGUI> Filter = (name, column) => AddText(name, transform,
                new Vector2(columns[column], 1f), new Vector2(columns[column + 1], 1f),
                new Vector2(column == 0 ? PADDING : 0f, filtersTop - FILTERS_HEIGHT), new Vector2(0f, filtersTop), 22f, TextAlignmentOptions.Center);
            globalText = Filter("Global", 0);
            friendsText = Filter("Friends", 1);
            mapText = Filter("Map", 2);
            tierText = Filter("Tier", 3);
            sizeText = Filter("Size", 4);

            rowsRect = AddRect("Rows", transform, Vector2.zero, Vector2.one, new Vector2(PADDING, PADDING), new Vector2(-PADDING, filtersTop - FILTERS_HEIGHT - 2f));
            statusText = AddText("Status", rowsRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 22f, TextAlignmentOptions.Center);

            var rowWidth = width - PADDING * 2f;
            var scale = rowWidth / ENTRY_SIZE.x;
            for (var i = 0; i < PAGE_SIZE; i++)
            {
                var row = CreateRow(scale, i);
                if (row != null)
                {
                    rows.Add(row);
                }
            }

            UpdateFilterTexts();
            if (isActiveAndEnabled)
            {
                Refresh();
            }
        }

        /// <summary>
        /// A copy of a visual of the game's window (its scripts removed), stretched over the panel or kept as laid out
        /// </summary>
        [HideFromIl2Cpp]
        private RectTransform AddVisual(GameObject widget, string path, bool stretch)
        {
            var source = widget.transform.Find(path);
            if (source == null)
            {
                return null;
            }

            var copy = CloneWithoutScripts(source.gameObject, transform);
            var rect = copy.GetComponent<RectTransform>();
            if (stretch)
            {
                SetRect(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>
        /// One of the game's leaderboard entries: its rank and score texts kept, its name and icons replaced by the party's
        /// characters and the run's modifiers
        /// </summary>
        [HideFromIl2Cpp]
        private Row CreateRow(float scale, int index)
        {
            var holder = new GameObject("Holder");
            holder.SetActive(false); // the copy's scripts must not run (Awake) before they are removed
            holder.transform.SetParent(rowsRect, false);
            try
            {
                var copy = Instantiate(entryPrefab, holder.transform);
                var entryUi = copy.GetComponent<LeaderboardEntryUi>();
                if (entryUi == null)
                {
                    return null;
                }

                var row = new Row
                {
                    Root = copy,
                    Rank = entryUi.rank,
                    Score = entryUi.score,
                    Hover = entryUi.hoverOverlay,
                    Outline = entryUi.outlineColor,
                    ColorDefault = entryUi.colorDefault,
                    ColorGold = entryUi.colorGold,
                };
                foreach (var hidden in new Component[] { entryUi.playerName, entryUi.playerIcon, entryUi.characterIcon, entryUi.localHighlight })
                {
                    if (hidden != null)
                    {
                        hidden.gameObject.SetActive(false);
                    }
                }
                RemoveScripts(copy);

                // The entry is scaled down to the panel: bigger texts keep them readable
                foreach (var text in new[] { row.Rank, row.Score })
                {
                    if (text != null)
                    {
                        text.enableAutoSizing = false;
                        text.fontSize *= TEXT_SCALE;
                        text.enableWordWrapping = false;
                        text.overflowMode = TextOverflowModes.Overflow;
                    }
                }

                row.Rect = copy.GetComponent<RectTransform>();
                row.Rect.SetParent(rowsRect, false);
                SetRect(row.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
                row.Rect.pivot = new Vector2(0.5f, 1f);
                row.Rect.sizeDelta = ENTRY_SIZE;
                row.Rect.localScale = new Vector3(scale, scale, 1f);
                row.Rect.anchoredPosition = new Vector2(0f, -index * (ENTRY_SIZE.y * scale + ROW_GAP));

                // Between the rank (left) and the score (right)
                row.Icons = AddRect("Icons", row.Rect, new Vector2(0.11f, 0f), new Vector2(0.72f, 1f), new Vector2(0f, 6f), new Vector2(0f, -6f));
                row.Hover?.SetActive(false);
                copy.SetActive(false);
                return row;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Co-op leaderboard: could not copy the game's leaderboard entry: {ex.Message}");
                return null;
            }
            finally
            {
                Destroy(holder);
            }
        }

        [HideFromIl2Cpp]
        private GameObject CloneWithoutScripts(GameObject source, Transform parent)
        {
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            var copy = Instantiate(source, holder.transform);
            RemoveScripts(copy);
            copy.transform.SetParent(parent, false);
            Destroy(holder);
            return copy;
        }

        /// <summary>
        /// Removes the game's scripts of the copied leaderboard visuals (they would fill the entry with the game's
        /// leaderboard, localize texts, run the weekly reset timer), keeps Unity's UI
        /// </summary>
        [HideFromIl2Cpp]
        private static void RemoveScripts(GameObject root)
        {
            foreach (var script in root.RuntimeGetComponentsInChildren<LeaderboardEntryUi>(true))
            {
                DestroyImmediate(script);
            }
            foreach (var script in root.RuntimeGetComponentsInChildren<LeaderboardResetTimer>(true))
            {
                DestroyImmediate(script);
            }
            foreach (var script in root.RuntimeGetComponentsInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true))
            {
                DestroyImmediate(script);
            }
        }

        public void OnEnable()
        {
            if (rowsRect != null)
            {
                Refresh();
            }
        }

        public void OnDisable()
        {
            HideTooltip();
        }

        public void OnDestroy()
        {
            if (tooltip != null)
            {
                Destroy(tooltip);
            }
        }

        public void Update()
        {
            ApplyPendingPage();
            HandleMouse();
        }

        #region Filters and paging

        [HideFromIl2Cpp]
        private void UpdateFilterTexts()
        {
            var map = CoopLeaderboardService.Maps[mapIndex];
            globalText.text = Loc.T("Global");
            friendsText.text = Loc.T("Friends");
            globalText.color = friendsOnly ? DIM_COLOR : SELECTED_COLOR;
            friendsText.color = friendsOnly ? SELECTED_COLOR : DIM_COLOR;
            mapText.text = "< " + Loc.T(map) + " >";
            tierText.text = Loc.F("Tier {0}", tier);
            sizeText.text = Loc.F("{0}P", partySize);

            var pages = Math.Max(1, (total + PAGE_SIZE - 1) / PAGE_SIZE);
            pageText.text = $"{page + 1}/{pages}";
            prevText.color = page > 0 ? TEXT_COLOR : DIM_COLOR;
            nextText.color = page < pages - 1 ? TEXT_COLOR : DIM_COLOR;
        }

        /// <summary>
        /// Left click: next value, right click: previous one
        /// </summary>
        [HideFromIl2Cpp]
        private void OnFilterClicked(TextMeshProUGUI filter, int step)
        {
            if (filter == globalText || filter == friendsText)
            {
                var wantFriends = filter == friendsText;
                if (wantFriends == friendsOnly)
                {
                    return;
                }
                friendsOnly = wantFriends;
            }
            else if (filter == mapText)
            {
                mapIndex = (mapIndex + step + CoopLeaderboardService.Maps.Length) % CoopLeaderboardService.Maps.Length;
            }
            else if (filter == tierText)
            {
                tier = (tier - 1 + step + CoopLeaderboardService.MAX_TIER) % CoopLeaderboardService.MAX_TIER + 1;
            }
            else if (filter == sizeText)
            {
                var sizes = CoopLeaderboardService.MAX_PARTY - CoopLeaderboardService.MIN_PARTY + 1;
                partySize = (partySize - CoopLeaderboardService.MIN_PARTY + step + sizes) % sizes + CoopLeaderboardService.MIN_PARTY;
            }

            page = 0;
            total = 0;
            PlaySelectSound();
            Refresh();
        }

        [HideFromIl2Cpp]
        private void OnPageClicked(int step)
        {
            var pages = Math.Max(1, (total + PAGE_SIZE - 1) / PAGE_SIZE);
            var newPage = Math.Clamp(page + step, 0, pages - 1);
            if (newPage == page)
            {
                return;
            }

            page = newPage;
            PlaySelectSound();
            Refresh();
        }

        [HideFromIl2Cpp]
        private static void PlaySelectSound()
        {
            AudioManager.Instance?.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
        }

        [HideFromIl2Cpp]
        private void Refresh()
        {
            HideTooltip();
            UpdateFilterTexts();
            foreach (var row in rows)
            {
                row.Root.SetActive(false);
            }
            statusText.text = Loc.T("Loading...");

            int id;
            lock (resultLock)
            {
                id = ++requestId;
                pendingPage = null;
                pendingError = null;
            }

            var (map, selectedTier, size, friends, offset) = (CoopLeaderboardService.Maps[mapIndex], tier, partySize, friendsOnly, page * PAGE_SIZE);
            Task.Run(async () =>
            {
                CoopLeaderboardPage result = null;
                string error = null;
                try
                {
                    result = await coopLeaderboardService.GetPage(map, selectedTier, size, friends, offset, PAGE_SIZE);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Could not load the co-op leaderboard: {ex.Message}");
                    error = ex.Message;
                }

                lock (resultLock)
                {
                    if (id == requestId)
                    {
                        pendingPage = result;
                        pendingError = error ?? "";
                    }
                }
            });
        }

        [HideFromIl2Cpp]
        private void ApplyPendingPage()
        {
            CoopLeaderboardPage result;
            string error;
            lock (resultLock)
            {
                if (pendingError == null)
                {
                    return;
                }
                result = pendingPage;
                error = pendingError;
                pendingPage = null;
                pendingError = null;
            }

            if (error.Length > 0 || result == null)
            {
                statusText.text = Loc.T("Could not load the leaderboard");
                return;
            }

            total = result.Total;
            UpdateFilterTexts();
            statusText.text = result.Entries.Count == 0 ? Loc.T("No runs yet") : "";

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.Entry = i < result.Entries.Count ? result.Entries[i] : null;
                row.Root.SetActive(row.Entry != null);
                if (row.Entry != null)
                {
                    FillRow(row);
                }
            }
        }

        [HideFromIl2Cpp]
        private void FillRow(Row row)
        {
            var entry = row.Entry;
            if (row.Rank != null)
            {
                row.Rank.text = "#" + entry.Rank;
            }
            if (row.Score != null)
            {
                row.Score.text = entry.Kills.ToString("N0").Replace(',', ' ');
            }
            if (row.Outline != null)
            {
                row.Outline.color = entry.Rank == 1 ? row.ColorGold : row.ColorDefault;
            }
            row.Hover?.SetActive(false);

            for (var i = row.Icons.childCount - 1; i >= 0; i--)
            {
                Destroy(row.Icons.GetChild(i).gameObject);
            }

            // Characters ----- modifiers, as many as fit the row's height and width
            var characters = (entry.Players ?? []).Select(p => GetCharacterIcon(p.Character)).ToList();
            var modifiers = Enum.GetValues<RunModifier>().Where(m => m != RunModifier.None && ((RunModifier)entry.Modifiers).HasFlag(m)).Select(GetModifierIcon).ToList();
            var width = ENTRY_SIZE.x * (0.72f - 0.11f);
            var height = ENTRY_SIZE.y - 12f;
            const float separator = 40f;
            const float gap = 4f;
            var count = characters.Count + modifiers.Count;
            var size = Mathf.Min(height, (width - (modifiers.Count > 0 ? separator : 0f) - gap * Math.Max(0, count - 1)) / Math.Max(1, count));

            var x = 0f;
            foreach (var texture in characters)
            {
                AddIcon(row.Icons, texture, x, size);
                x += size + gap;
            }
            if (modifiers.Count > 0)
            {
                var dash = AddText("Separator", row.Icons, Vector2.zero, new Vector2(0f, 1f), new Vector2(x, 0f), new Vector2(x + separator - gap, 0f), 30f, TextAlignmentOptions.Center);
                dash.text = "-";
                dash.color = DIM_COLOR;
                x += separator;
                foreach (var texture in modifiers)
                {
                    AddIcon(row.Icons, texture, x, size);
                    x += size + gap;
                }
            }
        }

        [HideFromIl2Cpp]
        private static void AddIcon(RectTransform parent, Texture texture, float x, float size)
        {
            var rect = AddRect("Icon", parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(x, 0f);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = texture != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);
            image.raycastTarget = false;
        }

        [HideFromIl2Cpp]
        private static Texture GetCharacterIcon(string character)
        {
            try
            {
                return Enum.TryParse<ECharacter>(character, out var eCharacter) ? DataManager.Instance?.GetCharacterData(eCharacter)?.icon : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The mod's icon of a run modifier (Assets/Modifiers, raw RGBA32 64x64 embedded in the mod)
        /// </summary>
        [HideFromIl2Cpp]
        private static Texture2D GetModifierIcon(RunModifier modifier)
        {
            if (modifierIcons.TryGetValue(modifier, out var cached) && cached != null)
            {
                return cached;
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Modifiers." + modifier);
            if (stream == null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            // The game has only the IL2CPP overloads (byte array as Il2CppStructArray, Apply with its two arguments), the
            // compile-time Unity references bind to others
            typeof(Texture2D).GetMethod(nameof(Texture2D.LoadRawTextureData), [typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>)])
                .Invoke(texture, [new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(bytes)]);
            typeof(Texture2D).GetMethod(nameof(Texture2D.Apply), [typeof(bool), typeof(bool)]).Invoke(texture, [false, false]);
            modifierIcons[modifier] = texture;
            return texture;
        }

        #endregion

        #region Mouse

        [HideFromIl2Cpp]
        private void HandleMouse()
        {
            if (!Rewired.ReInput.isReady)
            {
                return;
            }

            var mouse = Rewired.ReInput.controllers.Mouse;
            if (mouse == null)
            {
                return;
            }

            var position = mouse.screenPosition;
            var camera = GetCanvasCamera();

            Row hovered = null;
            foreach (var row in rows)
            {
                var isHovered = row.Root.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(row.Rect, position, camera);
                row.Hover?.SetActive(isHovered);
                if (isHovered)
                {
                    hovered = row;
                }
            }

            var left = mouse.GetButtonDown(0);
            var right = mouse.GetButtonDown(1);
            if (!left && !right)
            {
                return;
            }

            if (hovered != null && left)
            {
                if (tooltipRow == hovered)
                {
                    HideTooltip();
                }
                else
                {
                    ShowTooltip(hovered, position, camera);
                }
                return;
            }

            HideTooltip();

            var step = left ? 1 : -1;
            foreach (var filter in new[] { globalText, friendsText, mapText, tierText, sizeText })
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(filter.rectTransform, position, camera))
                {
                    OnFilterClicked(filter, step);
                    return;
                }
            }

            if (left && RectTransformUtility.RectangleContainsScreenPoint(prevText.rectTransform, position, camera))
            {
                OnPageClicked(-1);
            }
            else if (left && RectTransformUtility.RectangleContainsScreenPoint(nextText.rectTransform, position, camera))
            {
                OnPageClicked(1);
            }
        }

        /// <summary>
        /// The outermost canvas above the panel (GetComponentInParent is stripped from the game)
        /// </summary>
        [HideFromIl2Cpp]
        private Canvas GetRootCanvas()
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

        [HideFromIl2Cpp]
        private Camera GetCanvasCamera()
        {
            var canvas = GetRootCanvas();
            return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        /// <summary>
        /// The party's Steam names (and the run's modifiers) under the mouse
        /// </summary>
        [HideFromIl2Cpp]
        private void ShowTooltip(Row row, Vector2 screenPosition, Camera camera)
        {
            var canvas = GetRootCanvas();
            if (canvas == null)
            {
                return;
            }

            if (tooltip == null)
            {
                var rect = AddRect("CoopLeaderboardTooltip", canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                rect.pivot = new Vector2(0f, 1f);
                tooltip = rect.gameObject;
                var background = tooltip.AddComponent<Image>();
                background.color = new Color(0.08f, 0.06f, 0.1f, 0.95f);
                background.raycastTarget = false;
                tooltipText = AddText("Text", rect, Vector2.zero, Vector2.one, new Vector2(12f, 8f), new Vector2(-12f, -8f), 24f, TextAlignmentOptions.TopLeft);
            }

            var text = new StringBuilder();
            foreach (var player in row.Entry.Players ?? [])
            {
                text.Append($"{PlainText(player.SteamName ?? player.Name)} <color=#AAAAAA>({PlainText(player.Character)}, {player.Kills})</color>\n");
            }
            var modifiers = Scripts.Modal.NetplayOptionsModal.ModifierList.Where(m => ((RunModifier)row.Entry.Modifiers).HasFlag(m.Flag)).Select(m => Loc.T(m.Name)).ToList();
            if (modifiers.Count > 0)
            {
                text.Append($"<color=#FFD700>{string.Join(", ", modifiers)}</color>\n");
            }
            tooltipText.text = text.ToString().TrimEnd('\n');
            tooltipText.ForceMeshUpdate();

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            tooltipRect.sizeDelta = new Vector2(Mathf.Min(tooltipText.preferredWidth + 24f, 620f), tooltipText.preferredHeight + 16f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.GetComponent<RectTransform>(), screenPosition, camera, out var local);
            tooltipRect.anchorMin = tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tooltipRect.anchoredPosition = local + new Vector2(16f, -20f);
            tooltip.transform.SetAsLastSibling();
            tooltip.SetActive(true);
            tooltipRow = row;
        }

        [HideFromIl2Cpp]
        private void HideTooltip()
        {
            tooltip?.SetActive(false);
            tooltipRow = null;
        }

        [HideFromIl2Cpp]
        private static string PlainText(string text)
        {
            return (text ?? "").Replace("<", "").Replace(">", "");
        }

        #endregion

        #region UI helpers

        [HideFromIl2Cpp]
        private static RectTransform AddRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);
            return rect;
        }

        [HideFromIl2Cpp]
        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        [HideFromIl2Cpp]
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

        #endregion
    }
}
