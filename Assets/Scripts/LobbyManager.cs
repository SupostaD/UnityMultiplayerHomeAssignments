using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public const string GameModeSessionPropertyKey = "GameMode";
    public const string MapSessionPropertyKey = "Map";
    public const string SceneBuildIndexSessionPropertyKey = "SceneBuildIndex";
    private const int MinimumRoomPlayers = 2;
    private const int MaximumRoomPlayers = 10;

    [Serializable]
    private class LobbyMapOption
    {
        public string mapName = "Map";
        public int sceneBuildIndex = 1;
    }

    [Serializable]
    private class LobbyGameModeOption
    {
        public string gameModeName = "Deathmatch";
        public LobbyMapOption[] maps;
    }

    [Header("Fusion")]
    [SerializeField] private NetworkRunnerPrefabReferences runnerPrefab;
    [SerializeField] private int gameSceneBuildIndex = 1;

    [Header("Game Modes And Maps")]
    [SerializeField]
    private LobbyGameModeOption[] gameModeOptions =
    {
        new LobbyGameModeOption
        {
            gameModeName = "Deathmatch",
            maps = new[]
            {
                new LobbyMapOption { mapName = "Dust", sceneBuildIndex = 1 },
                new LobbyMapOption { mapName = "Office", sceneBuildIndex = 2 }
            }
        },
        new LobbyGameModeOption
        {
            gameModeName = "Race",
            maps = new[]
            {
                new LobbyMapOption { mapName = "Mario", sceneBuildIndex = 3 },
                new LobbyMapOption { mapName = "Ancient", sceneBuildIndex = 4 }
            }
        }
    };

    [Header("Panels")]
    [SerializeField] private LobbyPanelsUI panelsUI;

    [Header("Menu Panel Buttons")]
    [SerializeField] private Button openLobbyPanelButton;
    [SerializeField] private Button exitButton;
    
    [Header("Player Name Panel")]
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private Button confirmPlayerNameButton;
    [SerializeField] private Button backFromPlayerNameButton;

    [Header("Lobby Player Info")]
    [SerializeField] private NetworkObject lobbyPlayerInfoPrefab;

    [Header("Lobby Panel")]
    [SerializeField] private TMP_InputField lobbyNameInput;
    [SerializeField] private Button joinLobbyButton;
    [SerializeField] private Button backFromLobbyButton;

    [Header("Room Panel")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_Dropdown maxPlayersDropdown;
    [SerializeField] private Toggle hideRoomFromLobbyToggle;
    [SerializeField] private TMP_Dropdown gameModeDropdown;
    [SerializeField] private TMP_Dropdown mapDropdown;
    [SerializeField] private Button findRoomButton;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button leaveFromRoomsButton;
    [SerializeField] private TMP_Text roomPanelStatusText;

    [Header("Waiting Panel")]
    [SerializeField] private Button leaveFromWaitingButton;
    [SerializeField] private Button startGameButton;

    [Header("Room List Scroll View")]
    [SerializeField] private Transform roomListContent;
    [SerializeField] private RoomButtonUI roomButtonPrefab;

    [Header("Players List Scroll View")]
    [SerializeField] private Transform playersListContent;
    [SerializeField] private PlayerRowUI playerRowPrefab;

    [Header("Optional Texts")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text lobbyText;
    [SerializeField] private TMP_Text roomText;
    
    public static LobbyManager Instance { get; private set; }

    private NetworkRunner runner;
    private NetworkSceneManagerDefault sceneManager;
    private string localPlayerName;
    private Coroutine refreshPlayersCoroutine;

    private string currentLobbyName;
    private string currentRoomName;
    private string currentGameModeName;
    private string currentMapName;
    private int currentGameSceneBuildIndex;

    private bool isBusy;
    private bool isInLobby;
    private bool isInRoom;

    private readonly List<SessionInfo> cachedSessionList = new List<SessionInfo>();
    private bool hasAppliedRoomFilter;
    private int appliedFilterMaxPlayers;
    private string appliedFilterGameMode;
    private string appliedFilterMap;

    private void Awake()
    {
        Instance = this;

        CreateRunner();

        panelsUI.ShowMainMenu();

        openLobbyPanelButton.onClick.AddListener(OpenPlayerNamePanel);
        exitButton.onClick.AddListener(ExitGame);

        confirmPlayerNameButton.onClick.AddListener(ConfirmPlayerName);
        backFromPlayerNameButton.onClick.AddListener(BackFromPlayerNamePanel);

        joinLobbyButton.onClick.AddListener(JoinLobby);
        backFromLobbyButton.onClick.AddListener(BackFromLobbyPanel);

        if (findRoomButton != null)
            findRoomButton.onClick.AddListener(FindRoom);
        createRoomButton.onClick.AddListener(CreateRoom);
        leaveFromRoomsButton.onClick.AddListener(LeaveAll);

        leaveFromWaitingButton.onClick.AddListener(LeaveAll);
        startGameButton.onClick.AddListener(StartGameForAll);

        playerNameInput.onValueChanged.AddListener(_ => RefreshButtons());
        lobbyNameInput.onValueChanged.AddListener(_ => RefreshButtons());
        roomNameInput.onValueChanged.AddListener(_ => RefreshButtons());
        maxPlayersDropdown.onValueChanged.AddListener(OnMaxPlayersChanged);
        
        if (hideRoomFromLobbyToggle != null)
            hideRoomFromLobbyToggle.onValueChanged.AddListener(OnHideRoomFromLobbyChanged);

        if (gameModeDropdown != null)
            gameModeDropdown.onValueChanged.AddListener(OnCreateGameModeChanged);

        if (mapDropdown != null)
            mapDropdown.onValueChanged.AddListener(OnCreateMapChanged);

        InitializeGameModeAndMapDropdowns();

        ClearRoomList();
        ClearPlayersList();

        currentGameSceneBuildIndex = gameSceneBuildIndex;

        SetStatus("Ready");
        SetLobbyText("Lobby: Not joined");
        SetRoomText("Room: Not joined");

        RefreshButtons();
    }

    private void OnDestroy()
    {
        if (runner != null)
            runner.RemoveCallbacks(this);

        if (openLobbyPanelButton != null)
            openLobbyPanelButton.onClick.RemoveListener(OpenPlayerNamePanel);

        if (exitButton != null)
            exitButton.onClick.RemoveListener(ExitGame);

        if (confirmPlayerNameButton != null)
            confirmPlayerNameButton.onClick.RemoveListener(ConfirmPlayerName);

        if (backFromPlayerNameButton != null)
            backFromPlayerNameButton.onClick.RemoveListener(BackFromPlayerNamePanel);

        if (joinLobbyButton != null)
            joinLobbyButton.onClick.RemoveListener(JoinLobby);

        if (backFromLobbyButton != null)
            backFromLobbyButton.onClick.RemoveListener(BackFromLobbyPanel);

        if (findRoomButton != null)
            findRoomButton.onClick.RemoveListener(FindRoom);

        if (createRoomButton != null)
            createRoomButton.onClick.RemoveListener(CreateRoom);

        if (leaveFromRoomsButton != null)
            leaveFromRoomsButton.onClick.RemoveListener(LeaveAll);

        if (leaveFromWaitingButton != null)
            leaveFromWaitingButton.onClick.RemoveListener(LeaveAll);

        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(StartGameForAll);

        if (maxPlayersDropdown != null)
            maxPlayersDropdown.onValueChanged.RemoveListener(OnMaxPlayersChanged);

        if (hideRoomFromLobbyToggle != null)
            hideRoomFromLobbyToggle.onValueChanged.RemoveListener(OnHideRoomFromLobbyChanged);

        if (gameModeDropdown != null)
            gameModeDropdown.onValueChanged.RemoveListener(OnCreateGameModeChanged);

        if (mapDropdown != null)
            mapDropdown.onValueChanged.RemoveListener(OnCreateMapChanged);
    }

    private void CreateRunner()
    {
        if (runnerPrefab == null)
        {
            Debug.LogError("Runner Prefab is not assigned.");
            return;
        }

        NetworkRunnerPrefabReferences runnerReferences = Instantiate(runnerPrefab);
        runner = runnerReferences.Runner;
        sceneManager = runnerReferences.SceneManager;

        if (runner == null || sceneManager == null)
        {
            Debug.LogError(
                "NetworkRunner prefab references are incomplete. Assign Runner and Scene Manager on the prefab.",
                runnerReferences
            );
            return;
        }

        runner.name = "NetworkRunner";

        DontDestroyOnLoad(runner.gameObject);

        runner.AddCallbacks(this);
    }

    private void InitializeGameModeAndMapDropdowns()
    {
        PopulateGameModeDropdown(gameModeDropdown);
        PopulateCreateMapDropdown();
    }

    private void PopulateGameModeDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
            return;

        List<string> options = new List<string>();

        if (gameModeOptions != null)
        {
            foreach (LobbyGameModeOption gameModeOption in gameModeOptions)
            {
                if (gameModeOption == null)
                    continue;

                string gameModeName = NormalizeOptionName(gameModeOption.gameModeName);

                if (!string.IsNullOrWhiteSpace(gameModeName))
                    options.Add(gameModeName);
            }
        }

        SetDropdownOptions(dropdown, options, 0);
    }

    private void PopulateCreateMapDropdown()
    {
        if (mapDropdown == null)
            return;

        List<string> options = new List<string>();

        if (TryGetSelectedCreateGameMode(out LobbyGameModeOption gameModeOption) &&
            gameModeOption.maps != null)
        {
            foreach (LobbyMapOption mapOption in gameModeOption.maps)
            {
                if (mapOption == null)
                    continue;

                string mapName = NormalizeOptionName(mapOption.mapName);

                if (!string.IsNullOrWhiteSpace(mapName))
                    options.Add(mapName);
            }
        }

        SetDropdownOptions(mapDropdown, options, 0);
    }

    private void SetDropdownOptions(TMP_Dropdown dropdown, List<string> options, int selectedIndex)
    {
        if (dropdown == null)
            return;

        dropdown.ClearOptions();

        if (options != null && options.Count > 0)
            dropdown.AddOptions(options);

        int clampedIndex = options == null || options.Count == 0
            ? 0
            : Mathf.Clamp(selectedIndex, 0, options.Count - 1);

        dropdown.SetValueWithoutNotify(clampedIndex);
        dropdown.RefreshShownValue();
    }

    private void OnCreateGameModeChanged(int _)
    {
        ClearAppliedRoomFilter();
        PopulateCreateMapDropdown();
        ShowAllAvailableRooms();
        RefreshButtons();
    }

    private void OnMaxPlayersChanged(int _)
    {
        ClearAppliedRoomFilter();
        ShowAllAvailableRooms();
        RefreshButtons();
    }

    private void OnCreateMapChanged(int _)
    {
        ClearAppliedRoomFilter();
        ShowAllAvailableRooms();
        RefreshButtons();
    }

    private void OnHideRoomFromLobbyChanged(bool _)
    {
        RefreshButtons();
    }

    private bool TryGetSelectedCreateGameMode(out LobbyGameModeOption gameModeOption)
    {
        gameModeOption = null;

        if (gameModeOptions == null || gameModeOptions.Length == 0)
            return false;

        int selectedIndex = gameModeDropdown != null ? gameModeDropdown.value : 0;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, gameModeOptions.Length - 1);

        gameModeOption = gameModeOptions[selectedIndex];

        return gameModeOption != null &&
               !string.IsNullOrWhiteSpace(NormalizeOptionName(gameModeOption.gameModeName));
    }

    private bool TryGetSelectedCreateMap(LobbyGameModeOption gameModeOption, out LobbyMapOption mapOption)
    {
        mapOption = null;

        if (gameModeOption == null || gameModeOption.maps == null || gameModeOption.maps.Length == 0)
            return false;

        int selectedIndex = mapDropdown != null ? mapDropdown.value : 0;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, gameModeOption.maps.Length - 1);

        mapOption = gameModeOption.maps[selectedIndex];

        return mapOption != null &&
               !string.IsNullOrWhiteSpace(NormalizeOptionName(mapOption.mapName)) &&
               mapOption.sceneBuildIndex >= 0;
    }

    private Dictionary<string, SessionProperty> CreateRoomProperties(
        LobbyGameModeOption gameModeOption,
        LobbyMapOption mapOption)
    {
        return new Dictionary<string, SessionProperty>
        {
            { GameModeSessionPropertyKey, NormalizeOptionName(gameModeOption.gameModeName) },
            { MapSessionPropertyKey, NormalizeOptionName(mapOption.mapName) },
            { SceneBuildIndexSessionPropertyKey, mapOption.sceneBuildIndex }
        };
    }

    private void CacheCurrentSessionMetadata(SessionInfo session)
    {
        currentGameModeName = GetSessionPropertyText(session, GameModeSessionPropertyKey, string.Empty);
        currentMapName = GetSessionPropertyText(session, MapSessionPropertyKey, string.Empty);
        currentGameSceneBuildIndex = GetSessionPropertyInt(session, SceneBuildIndexSessionPropertyKey, gameSceneBuildIndex);
    }

    public static string GetSessionPropertyText(SessionInfo session, string propertyKey, string fallback)
    {
        if (session == null ||
            session.Properties == null ||
            string.IsNullOrWhiteSpace(propertyKey) ||
            !session.Properties.TryGetValue(propertyKey, out SessionProperty property))
        {
            return fallback;
        }

        object propertyValue = property.PropertyValue;

        if (propertyValue == null)
            return fallback;

        string text = propertyValue.ToString();

        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    private static int GetSessionPropertyInt(SessionInfo session, string propertyKey, int fallback)
    {
        if (session == null ||
            session.Properties == null ||
            string.IsNullOrWhiteSpace(propertyKey) ||
            !session.Properties.TryGetValue(propertyKey, out SessionProperty property))
        {
            return fallback;
        }

        object propertyValue = property.PropertyValue;

        if (propertyValue is int intValue)
            return intValue;

        if (propertyValue != null && int.TryParse(propertyValue.ToString(), out int parsedValue))
            return parsedValue;

        return fallback;
    }

    private string GetDropdownSelectedText(TMP_Dropdown dropdown)
    {
        if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
            return string.Empty;

        int selectedIndex = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
        TMP_Dropdown.OptionData selectedOption = dropdown.options[selectedIndex];

        return selectedOption == null ? string.Empty : NormalizeOptionName(selectedOption.text);
    }

    private string NormalizeOptionName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private void OpenPlayerNamePanel()
    {
        if (isBusy)
            return;

        panelsUI.ShowPlayerName();
        SetStatus("Enter player name");
        RefreshButtons();
    }
    
    private void ConfirmPlayerName()
    {
        if (isBusy)
            return;

        string playerName = playerNameInput.text.Trim();

        if (string.IsNullOrWhiteSpace(playerName))
        {
            SetStatus("Enter player name");
            RefreshButtons();
            return;
        }

        localPlayerName = playerName;

        panelsUI.ShowLobby();
        SetStatus("Enter lobby name");
        RefreshButtons();
    }

    private void BackFromPlayerNamePanel()
    {
        if (isBusy)
            return;

        localPlayerName = string.Empty;

        panelsUI.ShowMainMenu();
        SetStatus("Ready");
        RefreshButtons();
    }

    private void BackFromLobbyPanel()
    {
        if (isBusy)
            return;

        if (isInLobby || isInRoom)
            return;

        panelsUI.ShowPlayerName();
        SetStatus("Enter player name");
        RefreshButtons();
    }

    private async void JoinLobby()
    {
        if (string.IsNullOrWhiteSpace(localPlayerName))
        {
            SetStatus("Enter player name first");
            panelsUI.ShowPlayerName();
            RefreshButtons();
            return;
        }
        
        if (isBusy)
            return;

        string lobbyName = lobbyNameInput.text.Trim();

        if (string.IsNullOrWhiteSpace(lobbyName))
        {
            SetStatus("Enter lobby name");
            RefreshButtons();
            return;
        }

        if (runner == null)
        {
            CreateRunner();
        }

        isBusy = true;
        RefreshButtons();

        SetStatus("Joining lobby...");

        StartGameResult result = await runner.JoinSessionLobby(SessionLobby.Custom, lobbyName);
        
        isBusy = false;

        if (result.Ok)
        {
            currentLobbyName = lobbyName;
            isInLobby = true;
            isInRoom = false;
            ResetCurrentRoomMetadata();

            SetLobbyText("Lobby: " + currentLobbyName);
            SetRoomText("Room: Not joined");

            ClearRoomList();
            ClearPlayersList();
            cachedSessionList.Clear();
            ClearAppliedRoomFilter();

            panelsUI.ShowRooms();

            SetStatus("Joined lobby successfully");
            SetRoomSearchStatus("Waiting for room list...");
        }
        else
        {
            currentLobbyName = string.Empty;
            isInLobby = false;
            isInRoom = false;
            ResetCurrentRoomMetadata();
            ClearAppliedRoomFilter();

            SetStatus("Failed to join lobby: " + result.ShutdownReason);
        }

        RefreshButtons();
    }

    private async void CreateRoom()
    {
        if (isBusy)
            return;

        if (!isInLobby)
        {
            SetStatus("Join lobby first");
            RefreshButtons();
            return;
        }

        if (isInRoom)
        {
            SetStatus("You are already in a room");
            RefreshButtons();
            return;
        }

        string roomName = roomNameInput.text.Trim();

        if (string.IsNullOrWhiteSpace(roomName))
        {
            SetStatus("Enter room name");
            RefreshButtons();
            return;
        }

        if (!TryGetMaxPlayers(out int maxPlayers))
        {
            SetStatus("Select max players from 2 to 10");
            RefreshButtons();
            return;
        }

        if (!TryGetSelectedCreateGameMode(out LobbyGameModeOption selectedGameMode))
        {
            SetStatus("Select game mode");
            RefreshButtons();
            return;
        }

        if (!TryGetSelectedCreateMap(selectedGameMode, out LobbyMapOption selectedMap))
        {
            SetStatus("Select map");
            RefreshButtons();
            return;
        }

        bool hideRoomFromLobby = hideRoomFromLobbyToggle != null && hideRoomFromLobbyToggle.isOn;

        isBusy = true;
        RefreshButtons();

        SetStatus("Creating room...");

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            PlayerCount = maxPlayers,
            CustomLobbyName = currentLobbyName,
            SessionProperties = CreateRoomProperties(selectedGameMode, selectedMap),
            IsOpen = true,
            IsVisible = !hideRoomFromLobby,
            SceneManager = sceneManager
        });

        isBusy = false;

        if (result.Ok)
        {
            currentRoomName = roomName;
            currentGameModeName = NormalizeOptionName(selectedGameMode.gameModeName);
            currentMapName = NormalizeOptionName(selectedMap.mapName);
            currentGameSceneBuildIndex = selectedMap.sceneBuildIndex;
            isInRoom = true;

            SetRoomText(GetCurrentRoomText());

            ClearRoomList();
            cachedSessionList.Clear();
            ClearAppliedRoomFilter();

            panelsUI.ShowWaiting();

            SpawnLobbyPlayerInfo();
            RequestPlayersListRefresh();

            SetStatus("Room created successfully");
        }
        else
        {
            currentRoomName = string.Empty;
            ResetCurrentRoomMetadata();
            isInRoom = false;

            SetStatus("Failed to create room: " + result.ShutdownReason);
        }

        RefreshButtons();
    }

    private async void JoinRoom(SessionInfo session)
    {
        if (isBusy)
            return;

        if (!isInLobby)
        {
            SetStatus("Join lobby first");
            RefreshButtons();
            return;
        }

        if (isInRoom)
        {
            SetStatus("You are already in a room");
            RefreshButtons();
            return;
        }

        if (session == null)
        {
            SetStatus("Room does not exist");
            RefreshButtons();
            return;
        }

        if (!session.IsOpen || session.PlayerCount >= session.MaxPlayers)
        {
            SetStatus("Room is full or closed");
            RefreshButtons();
            return;
        }

        isBusy = true;
        RefreshButtons();

        SetStatus("Joining room...");

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = session.Name,
            CustomLobbyName = currentLobbyName,
            SceneManager = sceneManager
        });

        isBusy = false;

        if (result.Ok)
        {
            currentRoomName = session.Name;
            CacheCurrentSessionMetadata(session);
            isInRoom = true;

            SetRoomText(GetCurrentRoomText());

            ClearRoomList();
            cachedSessionList.Clear();
            ClearAppliedRoomFilter();

            panelsUI.ShowWaiting();

            SpawnLobbyPlayerInfo();
            RequestPlayersListRefresh();

            SetStatus("Joined room successfully");
        }
        else
        {
            currentRoomName = string.Empty;
            ResetCurrentRoomMetadata();
            isInRoom = false;

            SetStatus("Failed to join room: " + result.ShutdownReason);
        }

        RefreshButtons();
    }
    
    private void SpawnLobbyPlayerInfo()
    {
        if (runner == null)
            return;

        if (lobbyPlayerInfoPrefab == null)
        {
            Debug.LogError("Lobby Player Info Prefab is not assigned.");
            return;
        }

        NetworkObject existingObject = runner.GetPlayerObject(runner.LocalPlayer);

        if (existingObject != null)
            return;

        NetworkObject playerObject = runner.Spawn(
            lobbyPlayerInfoPrefab,
            Vector3.zero,
            Quaternion.identity,
            runner.LocalPlayer
        );

        if (playerObject == null)
        {
            Debug.LogError("Failed to spawn LobbyPlayerInfo object.");
            return;
        }

        LobbyPlayerInfo info =
            NetworkObjectBehaviourReferences.GetRequired<LobbyPlayerInfo>(
                playerObject,
                this
            );

        if (info != null)
        {
            info.Player = runner.LocalPlayer;
            info.PlayerName = localPlayerName;
        }

        runner.SetPlayerObject(runner.LocalPlayer, playerObject);
    }

    private void StartGameForAll()
    {
        if (isBusy)
            return;

        if (!isInRoom)
        {
            SetStatus("You are not in a room");
            RefreshButtons();
            return;
        }

        if (runner == null || !runner.IsSharedModeMasterClient)
        {
            SetStatus("Only MasterClient can start the game");
            RefreshButtons();
            return;
        }

        if (GetActivePlayerCount() < MinimumRoomPlayers)
        {
            SetStatus(
                $"At least {MinimumRoomPlayers} players are required"
            );
            RefreshButtons();
            return;
        }

        SetStatus("Loading game scene...");

        CloseSessionForGameStart();

        int sceneBuildIndex = currentGameSceneBuildIndex >= 0
            ? currentGameSceneBuildIndex
            : gameSceneBuildIndex;

        runner.LoadScene(SceneRef.FromIndex(sceneBuildIndex), LoadSceneMode.Single);
    }

    private void CloseSessionForGameStart()
    {
        if (runner == null || runner.SessionInfo == null)
            return;

        try
        {
            runner.SessionInfo.IsOpen = false;
            runner.SessionInfo.IsVisible = false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Failed to close session before game start: " + exception.Message);
        }
    }

    private async void LeaveAll()
    {
        if (isBusy)
            return;

        isBusy = true;
        RefreshButtons();

        SetStatus("Leaving...");

        isInLobby = false;
        isInRoom = false;

        currentLobbyName = string.Empty;
        currentRoomName = string.Empty;
        ResetCurrentRoomMetadata();

        ClearRoomList();
        ClearPlayersList();
        cachedSessionList.Clear();
        ClearAppliedRoomFilter();

        SetLobbyText("Lobby: Not joined");
        SetRoomText("Room: Not joined");

        if (runner != null)
        {
            runner.RemoveCallbacks(this);

            await runner.Shutdown();

            if (runner != null)
                Destroy(runner.gameObject);

            runner = null;
            sceneManager = null;
        }

        CreateRunner();

        isBusy = false;

        panelsUI.ShowMainMenu();

        SetStatus("Left");
        RefreshButtons();
    }

    private void ExitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void ResetCurrentRoomMetadata()
    {
        currentGameModeName = string.Empty;
        currentMapName = string.Empty;
        currentGameSceneBuildIndex = gameSceneBuildIndex;
    }

    private bool TryGetMaxPlayers(out int maxPlayers)
    {
        maxPlayers = 0;

        if (maxPlayersDropdown == null)
            return false;

        if (maxPlayersDropdown.options == null || maxPlayersDropdown.options.Count == 0)
            return false;

        string selectedText = maxPlayersDropdown.options[maxPlayersDropdown.value].text;

        if (!int.TryParse(selectedText, out maxPlayers))
        {
            maxPlayers = maxPlayersDropdown.value + 2;
        }

        return maxPlayers >= MinimumRoomPlayers &&
               maxPlayers <= MaximumRoomPlayers;
    }

    private int GetActivePlayerCount()
    {
        if (runner == null)
            return 0;

        int playerCount = 0;

        foreach (PlayerRef _ in runner.ActivePlayers)
            playerCount++;

        return playerCount;
    }

    private void RefreshButtons()
    {
        bool playerNameIsValid = playerNameInput != null &&
                                 !string.IsNullOrWhiteSpace(playerNameInput.text);

        bool lobbyNameIsValid = lobbyNameInput != null &&
                                !string.IsNullOrWhiteSpace(lobbyNameInput.text);

        bool roomNameIsValid = roomNameInput != null &&
                               !string.IsNullOrWhiteSpace(roomNameInput.text);

        bool maxPlayersIsValid = TryGetMaxPlayers(out _);
        bool gameModeIsValid = TryGetSelectedCreateGameMode(out LobbyGameModeOption selectedGameMode);
        bool mapIsValid = gameModeIsValid && TryGetSelectedCreateMap(selectedGameMode, out _);

        if (openLobbyPanelButton != null)
            openLobbyPanelButton.interactable = !isBusy && !isInLobby && !isInRoom;

        if (exitButton != null)
            exitButton.interactable = !isBusy;

        if (confirmPlayerNameButton != null)
            confirmPlayerNameButton.interactable = !isBusy &&
                                                   !isInLobby &&
                                                   !isInRoom &&
                                                   playerNameIsValid;

        if (backFromPlayerNameButton != null)
            backFromPlayerNameButton.interactable = !isBusy &&
                                                    !isInLobby &&
                                                    !isInRoom;

        if (joinLobbyButton != null)
            joinLobbyButton.interactable = !isBusy &&
                                           !isInLobby &&
                                           !isInRoom &&
                                           !string.IsNullOrWhiteSpace(localPlayerName) &&
                                           lobbyNameIsValid;

        if (backFromLobbyButton != null)
            backFromLobbyButton.interactable = !isBusy &&
                                               !isInLobby &&
                                               !isInRoom;

        if (createRoomButton != null)
            createRoomButton.interactable = !isBusy &&
                                            isInLobby &&
                                            !isInRoom &&
                                            roomNameIsValid &&
                                            maxPlayersIsValid &&
                                            gameModeIsValid &&
                                            mapIsValid;

        if (findRoomButton != null)
            findRoomButton.interactable = !isBusy &&
                                          isInLobby &&
                                          !isInRoom &&
                                          maxPlayersIsValid &&
                                          gameModeIsValid &&
                                          mapIsValid;

        if (leaveFromRoomsButton != null)
            leaveFromRoomsButton.interactable = !isBusy && isInLobby && !isInRoom;

        if (leaveFromWaitingButton != null)
            leaveFromWaitingButton.interactable = !isBusy && isInRoom;

        if (startGameButton != null)
            startGameButton.interactable = !isBusy &&
                                           isInRoom &&
                                           runner != null &&
                                           runner.IsSharedModeMasterClient &&
                                           GetActivePlayerCount() >=
                                           MinimumRoomPlayers;
    }

    private void ClearRoomList()
    {
        if (roomListContent == null)
            return;

        foreach (Transform child in roomListContent)
            Destroy(child.gameObject);
    }

    private void ClearPlayersList()
    {
        if (playersListContent == null)
            return;

        foreach (Transform child in playersListContent)
            Destroy(child.gameObject);
    }

    private void RefreshPlayersList()
    {
        ClearPlayersList();

        if (!isInRoom || runner == null || playerRowPrefab == null || playersListContent == null)
            return;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            string playerName = "Player " + player.PlayerId;

            NetworkObject playerObject = runner.GetPlayerObject(player);

            if (playerObject != null)
            {
                LobbyPlayerInfo info =
                    NetworkObjectBehaviourReferences.GetRequired<LobbyPlayerInfo>(
                        playerObject,
                        this
                    );

                if (info != null)
                {
                    string networkName = info.PlayerName.ToString();

                    if (!string.IsNullOrWhiteSpace(networkName))
                        playerName = networkName;
                }
            }

            PlayerRowUI row = Instantiate(playerRowPrefab, playersListContent);

            bool isLocal = player == runner.LocalPlayer;
            bool isMaster = isLocal && runner.IsSharedModeMasterClient;

            row.Init(playerName, player, isLocal, isMaster);
        }
    }
    
    public void RequestPlayersListRefresh()
    {
        if (refreshPlayersCoroutine != null)
            StopCoroutine(refreshPlayersCoroutine);

        refreshPlayersCoroutine = StartCoroutine(RefreshPlayersListRoutine());
    }

    private IEnumerator RefreshPlayersListRoutine()
    {
        for (int i = 0; i < 8; i++)
        {
            RefreshPlayersList();
            RefreshButtons();

            yield return new WaitForSeconds(0.25f);
        }

        refreshPlayersCoroutine = null;
    }

    private void SetStatus(string message)
    {
        Debug.Log(message);

        if (statusText != null)
            statusText.text = message;
    }

    private void SetLobbyText(string message)
    {
        if (lobbyText != null)
            lobbyText.text = message;
    }

    private void SetRoomText(string message)
    {
        if (roomText != null)
            roomText.text = message;
    }

    private void SetRoomSearchStatus(string message)
    {
        Debug.Log(message);

        if (roomPanelStatusText != null)
        {
            roomPanelStatusText.text = message;
            return;
        }

        if (statusText != null)
            statusText.text = message;
    }

    private string GetCurrentRoomText()
    {
        string message = "Room: " + currentRoomName;

        if (!string.IsNullOrWhiteSpace(currentGameModeName))
            message += " | " + currentGameModeName;

        if (!string.IsNullOrWhiteSpace(currentMapName))
            message += " | " + currentMapName;

        return message;
    }

    private void FindRoom()
    {
        if (isBusy)
            return;

        if (!isInLobby || isInRoom)
        {
            SetRoomSearchStatus("Join lobby first");
            RefreshButtons();
            return;
        }

        if (!TryGetMaxPlayers(out int maxPlayers))
        {
            SetRoomSearchStatus("Select max players from 2 to 10");
            RefreshButtons();
            return;
        }

        if (!TryGetSelectedCreateGameMode(out LobbyGameModeOption selectedGameMode))
        {
            SetRoomSearchStatus("Select game mode");
            RefreshButtons();
            return;
        }

        if (!TryGetSelectedCreateMap(selectedGameMode, out LobbyMapOption selectedMap))
        {
            SetRoomSearchStatus("Select map");
            RefreshButtons();
            return;
        }

        appliedFilterMaxPlayers = maxPlayers;
        appliedFilterGameMode = NormalizeOptionName(selectedGameMode.gameModeName);
        appliedFilterMap = NormalizeOptionName(selectedMap.mapName);
        hasAppliedRoomFilter = true;

        int foundRoomsCount = DrawRoomList(MatchesAppliedRoomFilter);

        if (foundRoomsCount > 0)
        {
            SetRoomSearchStatus("Found rooms: " + foundRoomsCount + " | " + GetAppliedRoomFilterText());
        }
        else
        {
            string filterText = GetAppliedRoomFilterText();
            ClearAppliedRoomFilter();
            int availableRoomsCount = DrawRoomList(IsAvailableRoom);
            SetRoomSearchStatus(
                "No rooms found for " +
                filterText +
                ". Showing all available rooms: " +
                availableRoomsCount
            );
        }

        RefreshButtons();
    }

    private void RefreshRoomListFromCache()
    {
        if (!isInLobby || isInRoom || panelsUI.CurrentState != LobbyPanelState.Rooms)
            return;

        if (hasAppliedRoomFilter)
        {
            int filteredRoomsCount = DrawRoomList(MatchesAppliedRoomFilter);

            if (filteredRoomsCount > 0)
            {
                SetRoomSearchStatus("Found rooms: " + filteredRoomsCount + " | " + GetAppliedRoomFilterText());
                return;
            }

            string filterText = GetAppliedRoomFilterText();
            ClearAppliedRoomFilter();
            int availableRoomsCount = DrawRoomList(IsAvailableRoom);
            SetRoomSearchStatus("No rooms found for " + filterText + ". Showing all available rooms: " + availableRoomsCount);
            return;
        }

        ShowAllAvailableRooms();
    }

    private void ShowAllAvailableRooms()
    {
        if (!isInLobby || isInRoom || panelsUI.CurrentState != LobbyPanelState.Rooms)
            return;

        int availableRoomsCount = DrawRoomList(IsAvailableRoom);

        SetRoomSearchStatus(availableRoomsCount > 0
            ? "Showing all available rooms: " + availableRoomsCount
            : "No available rooms");
    }

    private int DrawRoomList(Predicate<SessionInfo> roomFilter)
    {
        ClearRoomList();

        int roomsCount = 0;

        foreach (SessionInfo session in cachedSessionList)
        {
            if (roomFilter != null && !roomFilter(session))
                continue;

            RoomButtonUI roomButton = Instantiate(roomButtonPrefab, roomListContent);
            roomButton.Init(session, JoinRoom);
            roomsCount++;
        }

        return roomsCount;
    }

    private bool MatchesAppliedRoomFilter(SessionInfo session)
    {
        if (!IsAvailableRoom(session))
            return false;

        if (session.MaxPlayers != appliedFilterMaxPlayers)
            return false;

        if (!SessionPropertyEquals(session, GameModeSessionPropertyKey, appliedFilterGameMode))
            return false;

        if (!SessionPropertyEquals(session, MapSessionPropertyKey, appliedFilterMap))
            return false;

        return true;
    }

    private bool IsAvailableRoom(SessionInfo session)
    {
        if (session == null)
            return false;

        if (!session.IsVisible)
            return false;

        if (!session.IsOpen)
            return false;

        if (session.PlayerCount >= session.MaxPlayers)
            return false;

        return true;
    }

    private bool SessionPropertyEquals(SessionInfo session, string propertyKey, string expectedValue)
    {
        string actualValue = GetSessionPropertyText(session, propertyKey, string.Empty);

        return string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);
    }

    private string GetAppliedRoomFilterText()
    {
        return GetRoomFilterText(appliedFilterMaxPlayers, appliedFilterGameMode, appliedFilterMap);
    }

    private string GetRoomFilterText(int maxPlayers, string gameModeName, string mapName)
    {
        return "Max players: " + maxPlayers +
               " | Game mode: " + gameModeName +
               " | Map: " + mapName;
    }

    private void ClearAppliedRoomFilter()
    {
        hasAppliedRoomFilter = false;
        appliedFilterMaxPlayers = 0;
        appliedFilterGameMode = string.Empty;
        appliedFilterMap = string.Empty;
    }

    public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList)
    {
        cachedSessionList.Clear();

        if (sessionList != null)
            cachedSessionList.AddRange(sessionList);

        RefreshRoomListFromCache();
    }

    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player)
    {
        if (!isInRoom)
            return;

        SetStatus("Player joined: " + player.PlayerId);

        RefreshPlayersList();
        RefreshButtons();
    }

    public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player)
    {
        if (!isInRoom)
            return;

        SetStatus("Player left: " + player.PlayerId);

        RefreshPlayersList();
        RefreshButtons();
    }

    public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
    {
        isInLobby = false;
        isInRoom = false;

        currentLobbyName = string.Empty;
        currentRoomName = string.Empty;
        ResetCurrentRoomMetadata();

        ClearRoomList();
        ClearPlayersList();
        cachedSessionList.Clear();
        ClearAppliedRoomFilter();

        SetLobbyText("Lobby: Not joined");
        SetRoomText("Room: Not joined");
        SetStatus("Shutdown: " + shutdownReason);

        if (panelsUI != null)
            panelsUI.ShowMainMenu();

        RefreshButtons();
    }

    public void OnConnectedToServer(NetworkRunner callbackRunner)
    {
        SetStatus("Connected to server");
    }

    public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        SetStatus("Connection failed: " + reason);
        RefreshButtons();
    }

    public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason)
    {
        SetStatus("Disconnected: " + reason);

        isInLobby = false;
        isInRoom = false;
        ResetCurrentRoomMetadata();

        ClearRoomList();
        ClearPlayersList();
        cachedSessionList.Clear();
        ClearAppliedRoomFilter();

        if (panelsUI != null)
            panelsUI.ShowMainMenu();

        RefreshButtons();
    }

    public void OnSceneLoadDone(NetworkRunner callbackRunner)
    {
        SetStatus("Scene loaded");
    }

    public void OnSceneLoadStart(NetworkRunner callbackRunner)
    {
        SetStatus("Loading scene...");
    }

    public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
    public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnUserSimulationMessage(NetworkRunner callbackRunner, SimulationMessagePtr message) { }
}

