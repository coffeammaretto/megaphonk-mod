using Assets.Scripts.Settings___Saves.SaveFiles;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Configuration;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts.Button;
using MegabonkTogether.Scripts.Modal;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace MegabonkTogether.Scripts
{
    internal class NetworkMenuTab : ModalBase
    {
        private CustomButton randomButton;

        // Random matchmaking needs the Megabonk Together server, whose players run an incompatible mod
        private const bool IS_RANDOM_MATCHMAKING_ENABLED = false;
        private const int MAX_FRIEND_ROWS = 4;
        private const float FRIENDS_REFRESH_SECONDS = 2f;
        private CustomButton friendliesButton;
        private CustomButton closeButton;
        private CustomButton stopButton;
        private TMP_InputField playerNameInput;
        private MainMenu mainMenu;
        private GameObject label;
        private bool wasInputFocused;
        private Coroutine connectionCoroutine;
        private ProfanityFilter.ProfanityFilter filter;

        private GameObject friendliesTitle;
        private CustomButton joinFriendsButton;
        private GameObject noFriendsLabel;
        private readonly System.Collections.Generic.List<GameObject> friendRows = [];
        private float nextFriendsRefreshAt;
        private bool isFriendsScreenVisible;
        private CustomButton friendliesBackButton;

        protected void Awake()
        {
            filter = new ProfanityFilter.ProfanityFilter();
        }

        public void SetMainMenu(MainMenu menu)
        {
            mainMenu = menu;
        }

        protected override void OnUICreated()
        {
            CreateCloseButton();
            CreatePlayerNameInput();
            CreateMatchButtons();
            CreateStopButton();
            CreateFriendliesUI();
        }

        private void CreateCloseButton()
        {
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

            var localizeStringEvent = buttonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEvent != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEvent);
            }

            closeButton = buttonObj.AddComponent<CustomButton>();
            closeButton.SetOnClickAction(OnCloseClicked);

            var textWrapper = buttonObj.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.text = Loc.T("Close");
            textWrapper.t_text.fontSize = 36;

            var rectTransform = buttonObj.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, -200f);
            rectTransform.sizeDelta = new Vector2(300, 70);
        }

        private void CreatePlayerNameInput()
        {
            var inputObj = new GameObject("PlayerNameInput");
            inputObj.transform.SetParent(panel.transform, false);

            var rectTransform = inputObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.7f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.7f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(300, 50);

            var image = inputObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            playerNameInput = inputObj.AddComponent<TMP_InputField>();
            playerNameInput.textComponent = CreateInputText(inputObj);
            playerNameInput.placeholder = CreatePlaceholderText(inputObj);
            playerNameInput.text = ModConfig.PlayerName.Value;
            playerNameInput.characterLimit = 20;

            playerNameInput.caretWidth = 3;
            playerNameInput.caretColor = Color.white;
            playerNameInput.customCaretColor = true;
            playerNameInput.caretBlinkRate = 0.85f;

            playerNameInput.selectionColor = new Color(0.65f, 0.8f, 1f, 0.5f);

            // Enable/disable to force caret initialization (Thanks random user on Stack exchange)
            playerNameInput.enabled = false;
            playerNameInput.enabled = true;

            label = new GameObject("Label");
            label.transform.SetParent(panel.transform, false);

            var labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.75f);
            labelRect.anchorMax = new Vector2(0.5f, 0.75f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0, 25);
            labelRect.sizeDelta = new Vector2(300, 40);

            var labelText = label.AddComponent<TextMeshProUGUI>();
            labelText.text = Loc.T("Player Name:");
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.fontSize = 50;
            labelText.color = Color.white;
        }

        private TextMeshProUGUI CreateInputText(GameObject parent)
        {
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(parent.transform, false);

            var rectTransform = textObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Left;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.fontSize = 35;
            text.color = Color.white;
            text.margin = new Vector4(10, 0, 10, 0);

            return text;
        }

        private TextMeshProUGUI CreatePlaceholderText(GameObject parent)
        {
            var placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(parent.transform, false);

            var rectTransform = placeholderObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            var text = placeholderObj.AddComponent<TextMeshProUGUI>();
            text.text = Loc.T("Enter your name...");
            text.alignment = TextAlignmentOptions.Left;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.fontSize = 24;
            text.color = new Color(1f, 1f, 1f, 0.3f);
            text.margin = new Vector4(10, 0, 10, 0);

            return text;
        }

        private void CreateStopButton()
        {
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

            var localizeStringEvent = buttonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEvent != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEvent);
            }

            stopButton = buttonObj.AddComponent<CustomButton>();
            stopButton.SetOnClickAction(OnStopClicked);

            var textWrapper = buttonObj.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.fontSize = 36;

            var rectTransform = buttonObj.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, -220f);
            rectTransform.sizeDelta = new Vector2(300, 70);

            stopButton.gameObject.SetActive(false);
        }

        private void CreateMatchButtons()
        {
            var randomButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            randomButtonObj.transform.SetParent(panel.transform, false);

            var originalRandomButton = randomButtonObj.GetComponent<MyButtonNormal>();
            if (originalRandomButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalRandomButton);
            }

            UnityEngine.UI.Button button = randomButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick = new();
            }

            var localizeStringEvent = randomButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEvent != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEvent);
            }

            randomButton = randomButtonObj.AddComponent<CustomButton>();
            randomButton.SetOnClickAction(OnRandomClicked);

            var randomTextWrapper = randomButtonObj.GetComponent<ButtonTextWrapper>();
            randomTextWrapper.t_text.text = Loc.T("Random");
            randomTextWrapper.t_text.fontSize = 36;

            var randomRectTransform = randomButtonObj.GetComponent<RectTransform>();
            randomRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            randomRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            randomRectTransform.pivot = new Vector2(0.5f, 0.5f);
            randomRectTransform.anchoredPosition = new Vector2(-180f, -100f);
            randomRectTransform.sizeDelta = new Vector2(300, 70);
            randomButtonObj.SetActive(IS_RANDOM_MATCHMAKING_ENABLED);

            var friendliesButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            friendliesButtonObj.transform.SetParent(panel.transform, false);

            var originalFriendliesButton = friendliesButtonObj.GetComponent<MyButtonNormal>();
            if (originalFriendliesButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalFriendliesButton);
            }

            UnityEngine.UI.Button butt = friendliesButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (butt != null)
            {
                butt.onClick = new();
            }

            var localizeStringEventFriendlies = friendliesButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEventFriendlies != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEventFriendlies);
            }

            friendliesButton = friendliesButtonObj.AddComponent<CustomButton>();
            friendliesButton.SetOnClickAction(OnHostClicked);

            var friendliesTextWrapper = friendliesButtonObj.GetComponent<ButtonTextWrapper>();
            friendliesTextWrapper.t_text.text = Loc.T("Host");
            friendliesTextWrapper.t_text.fontSize = 36;

            var friendliesRectTransform = friendliesButtonObj.GetComponent<RectTransform>();
            friendliesRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            friendliesRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            friendliesRectTransform.pivot = new Vector2(0.5f, 0.5f);
            friendliesRectTransform.anchoredPosition = new Vector2(IS_RANDOM_MATCHMAKING_ENABLED ? 180f : -170f, -100f);
            friendliesRectTransform.sizeDelta = new Vector2(300, 70);

            joinFriendsButton = CreateMenuButton("Join", 36, new Vector2(170f, -100f), new Vector2(300, 70), OnFriendsClicked);
        }

        private void CreateFriendliesUI()
        {
            friendliesTitle = new GameObject("FriendliesTitle");
            friendliesTitle.transform.SetParent(panel.transform, false);

            var titleRect = friendliesTitle.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.85f);
            titleRect.anchorMax = new Vector2(0.5f, 0.85f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.sizeDelta = new Vector2(400, 60);

            var titleText = friendliesTitle.AddComponent<TextMeshProUGUI>();
            titleText.text = Loc.T("Friends");
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontSize = 60;
            titleText.color = Color.white;

            friendliesTitle.SetActive(false);

            var backButtonObj = GameObject.Instantiate(mainMenu.btnPlay.gameObject);
            backButtonObj.transform.SetParent(panel.transform, false);

            var originalBackButton = backButtonObj.GetComponent<MyButtonNormal>();
            if (originalBackButton != null)
            {
                UnityEngine.Object.DestroyImmediate(originalBackButton);
            }

            UnityEngine.UI.Button backButton = backButtonObj.GetComponentInChildren<UnityEngine.UI.Button>();
            if (backButton != null)
            {
                backButton.onClick = new();
            }

            var localizeStringEventBack = backButtonObj.GetComponentInChildren<LocalizeStringEvent>();
            if (localizeStringEventBack != null)
            {
                UnityEngine.Object.DestroyImmediate(localizeStringEventBack);
            }

            friendliesBackButton = backButtonObj.AddComponent<CustomButton>();
            friendliesBackButton.SetOnClickAction(OnFriendliesBackClicked);

            var backTextWrapper = backButtonObj.GetComponent<ButtonTextWrapper>();
            backTextWrapper.t_text.text = Loc.T("Back");
            backTextWrapper.t_text.fontSize = 36;

            var backRectTransform = backButtonObj.GetComponent<RectTransform>();
            backRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            backRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            backRectTransform.pivot = new Vector2(0.5f, 0.5f);
            backRectTransform.anchoredPosition = new Vector2(0, -200f);
            backRectTransform.sizeDelta = new Vector2(300, 70);

            friendliesBackButton.gameObject.SetActive(false);
        }

        protected override void Update()
        {
            base.Update();

            if (playerNameInput == null) return;

            bool isFocused = playerNameInput.isFocused;

            if (wasInputFocused && !isFocused)
            {
                OnPlayerNameEndEdit();
            }
            wasInputFocused = isFocused;

            if (isFriendsScreenVisible && Time.realtimeSinceStartup >= nextFriendsRefreshAt)
            {
                nextFriendsRefreshAt = Time.realtimeSinceStartup + FRIENDS_REFRESH_SECONDS;
                RefreshFriends();
            }
        }

        /// <summary>
        /// A button cloned from the main menu's play button, with the game's own click logic replaced
        /// </summary>
        private CustomButton CreateMenuButton(string text, float fontSize, Vector2 position, Vector2 size, System.Action onClick)
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
            customButton.SetOnClickAction(onClick);

            var textWrapper = buttonObj.GetComponent<ButtonTextWrapper>();
            textWrapper.t_text.text = Loc.T(text);
            textWrapper.t_text.fontSize = fontSize;

            var rect = buttonObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return customButton;
        }

        private void OnPlayerNameEndEdit()
        {
            if (playerNameInput == null) return;

            string newName = playerNameInput.text;

            if (string.IsNullOrWhiteSpace(newName))
            {
                playerNameInput.text = ModConfig.PlayerName.Value;
                return;
            }

            if (filter.IsProfanity(newName))
            {
                newName = filter.CensorString(newName);
                playerNameInput.text = newName;
            }

            newName = newName.Trim();

            ModConfig.PlayerName.Value = newName;
            ModConfig.Save();
            Plugin.Log.LogInfo($"Player name updated to: {ModConfig.PlayerName.Value}");
        }

        private void OnCloseClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
            CloseModal();
        }

        private void OnRandomClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            Plugin.Instance.Mode.Mode = NetworkModeType.Random;

            UpdateModalContents(false);

            ShowLoader("Connecting...");

            Plugin.Instance.NetworkHandler.HandleNetworking();
            connectionCoroutine = CoroutineRunner.Instance.Run(HandleConnectionStatus());
        }

        private void OnFriendsClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            Plugin.Instance.Mode.Mode = NetworkModeType.Friendlies;

            UpdateModalContents(false);
            UpdateFriendliesUI(true);
        }

        private void OnHostClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            Plugin.Instance.Mode.Mode = NetworkModeType.Friendlies;
            Plugin.Instance.Mode.Role = Role.Host;

            UpdateModalContents(false);

            ShowLoader("Connecting...");

            Plugin.Instance.NetworkHandler.HandleNetworking();
            connectionCoroutine = CoroutineRunner.Instance.Run(HandleFriendlies());
        }

        private void OnJoinFriendClicked(string code)
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            Plugin.Instance.Mode.Mode = NetworkModeType.Friendlies;
            Plugin.Instance.Mode.Role = Role.Client;
            Plugin.Instance.Mode.RoomCode = code;

            UpdateFriendliesUI(false);

            ShowLoader("Joining room...");

            Plugin.Instance.NetworkHandler.HandleNetworking();
            connectionCoroutine = CoroutineRunner.Instance.Run(HandleFriendlies());
        }

        private void OnFriendliesBackClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            UpdateFriendliesUI(false);
            UpdateModalContents(true);
        }

        private void OnStopClicked()
        {
            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            if (connectionCoroutine != null)
            {
                CoroutineRunner.Instance.StopCoroutine(connectionCoroutine);
                connectionCoroutine = null;
            }

            Plugin.Instance.NetworkHandler.ResetNetworking();

            HideLoader();

            UpdateModalContents(true);
            stopButton.gameObject.SetActive(false);

            var closeTextWrapper = closeButton.gameObject.GetComponent<ButtonTextWrapper>();
            closeTextWrapper.t_text.text = Loc.T("Close");
            SetStatusText("");
        }

        private void UpdateModalContents(bool isVisible)
        {
            randomButton.gameObject.SetActive(isVisible && IS_RANDOM_MATCHMAKING_ENABLED);
            friendliesButton.gameObject.SetActive(isVisible);
            joinFriendsButton.gameObject.SetActive(isVisible);
            closeButton.gameObject.SetActive(isVisible);
            playerNameInput.gameObject.SetActive(isVisible);
            label.SetActive(isVisible);
        }

        private void UpdateFriendliesUI(bool isVisible)
        {
            isFriendsScreenVisible = isVisible;
            friendliesTitle.SetActive(isVisible);
            friendliesBackButton.gameObject.SetActive(isVisible);
            if (isVisible)
            {
                nextFriendsRefreshAt = 0f;
            }
            else
            {
                ClearFriendRows();
            }
        }

        private void ClearFriendRows()
        {
            foreach (var row in friendRows)
            {
                Destroy(row);
            }
            friendRows.Clear();

            if (noFriendsLabel != null)
            {
                Destroy(noFriendsLabel);
                noFriendsLabel = null;
            }
        }

        /// <summary>
        /// Friends who host a room (published in their Steam rich presence): one button each, refreshed while the screen is open
        /// </summary>
        private void RefreshFriends()
        {
            ClearFriendRows();

            var hosting = Steam.SteamApi.Initialize() ? Steam.SteamApi.GetHostingFriends() : [];
            if (hosting.Count == 0)
            {
                noFriendsLabel = new GameObject("NoFriendsLabel");
                noFriendsLabel.transform.SetParent(panel.transform, false);
                var rect = noFriendsLabel.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 40f);
                rect.sizeDelta = new Vector2(600, 120);
                var text = noFriendsLabel.AddComponent<TextMeshProUGUI>();
                text.text = Loc.T("No Steam friends are hosting right now.\nAsk a friend to press Host.");
                text.alignment = TextAlignmentOptions.Center;
                text.fontSize = 28;
                text.color = Color.white;
                return;
            }

            for (var i = 0; i < hosting.Count && i < MAX_FRIEND_ROWS; i++)
            {
                var (name, code) = hosting[i];
                var row = CreateMenuButton(name, 32, new Vector2(0f, 110f - i * 75f), new Vector2(450, 60), () => OnJoinFriendClicked(code));
                friendRows.Add(row.gameObject);
            }
        }

        private IEnumerator HandleConnectionStatus()
        {
            stopButton.gameObject.SetActive(true);
            var stopTextWrapper = stopButton.gameObject.GetComponent<ButtonTextWrapper>();
            stopTextWrapper.t_text.text = Loc.T("Stop");

            float timeout = 30f;
            float elapsed = 0f;

            while (elapsed < timeout && !Plugin.Instance.NetworkHandler.IsConnectedToMatchMaker.HasValue)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (!Plugin.Instance.NetworkHandler.IsConnectedToMatchMaker.Value)
            {
                HideLoader();
                SetStatusText(Loc.F("Failed to connect to server: {0}", Loc.T(Plugin.Instance.NetworkHandler.MatchMakerFailureMessage)));
                Plugin.Instance.NetworkHandler.ResetNetworking();

                stopButton.gameObject.SetActive(false);
                yield return new WaitForSeconds(4f);
                UpdateModalContents(true);
                SetStatusText("");

                yield break;
            }

            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);
            var sharedExpStatus = ModConfig.EnabledSharedExperience.Value
                ? "<color=green>ON</color>"
                : "<color=red>OFF</color>";
            SetStatusText($"Waiting for a match... \n (You can only match people with shared experience {sharedExpStatus})");

            while (!Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue)
            {
                if (Plugin.Instance.NetworkHandler.IsNetworkInterruptedStatus)
                {
                    HideLoader();
                    SetStatusText(Loc.F("Network interrupted, please try again: {0}", Loc.T(Plugin.Instance.NetworkHandler.MatchMakerFailureMessage)));
                    Plugin.Instance.NetworkHandler.ResetNetworking();

                    stopButton.gameObject.SetActive(false);

                    yield return new WaitForSeconds(3f);
                    UpdateModalContents(true);
                    SetStatusText("");
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }

            if (!Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue || !Plugin.Instance.NetworkHandler.HasFoundMatch.Value)
            {
                HideLoader();
                SetStatusText(Loc.F("Failed to connect: {0}", Loc.T(Plugin.Instance.NetworkHandler.MatchMakerFailureMessage)));
                Plugin.Instance.NetworkHandler.ResetNetworking();
                stopButton.gameObject.SetActive(false);
                yield return new WaitForSeconds(4f);
                UpdateModalContents(true);
                SetStatusText("");
                yield break;
            }

            AudioManager.Instance.PlaySfx(AudioManager.Instance.purchaseSfx.sounds[0]);
            HideLoader();
            SetStatusText("Match found!");
            stopButton.gameObject.SetActive(false);

            mainMenu.GoToCharacterSelection();

            var characterMenu = WindowManager.activeWindow as CharacterMenu;
            if (characterMenu != null)
            {
                characterMenu.selectedButton = characterMenu.characterButtons[0];
                characterMenu.b_confirm.SetInteractable(false);
            }

            var role = Plugin.Instance.NetworkHandler.IsHost ? "Host" : "Client";
            var lobbySize = Plugin.Instance.NetworkHandler.GetLobbySize();
            Plugin.StartNotification(("MegabonkTogether", "MatchSuccess"), ("MegabonkTogether", "MatchSuccessDesc"), [role, lobbySize.ToString()]);

            yield return new WaitForSeconds(1f);
            CloseModal();
        }

        private IEnumerator HandleFriendlies()
        {
            stopButton.gameObject.SetActive(true);
            var stopTextWrapper = stopButton.gameObject.GetComponent<ButtonTextWrapper>();
            stopTextWrapper.t_text.text = Loc.T("Stop");

            float timeout = 30f;
            float elapsed = 0f;

            while (elapsed < timeout && !Plugin.Instance.NetworkHandler.IsConnectedToMatchMaker.HasValue)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (!Plugin.Instance.NetworkHandler.IsConnectedToMatchMaker.Value)
            {
                HideLoader();
                SetStatusText(Loc.F("Failed to connect to server: {0}", Loc.T(Plugin.Instance.NetworkHandler.MatchMakerFailureMessage)));
                Plugin.Instance.NetworkHandler.ResetNetworking();

                stopButton.gameObject.SetActive(false);
                yield return new WaitForSeconds(4f);
                UpdateModalContents(true);
                SetStatusText("");

                yield break;
            }

            AudioManager.Instance.PlaySfx(AudioManager.Instance.uiSelect.sounds[0]);

            elapsed = 0f;

            while (elapsed < timeout && !Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue)
            {
                if (Plugin.Instance.NetworkHandler.IsNetworkInterruptedStatus)
                {
                    HideLoader();
                    SetStatusText(Loc.F("Network interrupted, please try again: {0}", Loc.T(Plugin.Instance.NetworkHandler.MatchMakerFailureMessage)));
                    Plugin.Instance.NetworkHandler.ResetNetworking();

                    stopButton.gameObject.SetActive(false);

                    yield return new WaitForSeconds(3f);
                    UpdateModalContents(true);
                    SetStatusText("");
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (!Plugin.Instance.NetworkHandler.HasFoundMatch.HasValue || !Plugin.Instance.NetworkHandler.HasFoundMatch.Value)
            {
                HideLoader();
                SetStatusText(Loc.F("Failed to connect: {0}", Loc.T(Plugin.Instance.NetworkHandler.MatchMakerFailureMessage)));
                Plugin.Instance.NetworkHandler.ResetNetworking();
                stopButton.gameObject.SetActive(false);
                yield return new WaitForSeconds(4f);
                UpdateModalContents(true);
                SetStatusText("");
                yield break;
            }

            AudioManager.Instance.PlaySfx(AudioManager.Instance.purchaseSfx.sounds[0]);
            HideLoader();
            SetStatusText("Joined!");
            stopButton.gameObject.SetActive(false);

            mainMenu.GoToCharacterSelection();

            var characterMenu = WindowManager.activeWindow as CharacterMenu;
            if (characterMenu != null)
            {
                characterMenu.selectedButton = characterMenu.characterButtons[0];
                characterMenu.b_confirm.SetInteractable(false);
            }

            if (Plugin.Instance.NetworkHandler.IsHost)
            {
                Plugin.StartNotification(("MegabonkTogether", "FriendliesHostSuccess"), ("MegabonkTogether", "FriendliesHostSuccessDesc"), []);
            }
            else
            {
                Plugin.StartNotification(("MegabonkTogether", "FriendliesClientSuccess"), ("MegabonkTogether", "FriendliesClientSuccessDesc"), [""]);
            }

            yield return new WaitForSeconds(1f);
            CloseModal();
        }
    }
}