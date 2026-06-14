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
    [Header("Fusion")]
    [SerializeField] private NetworkRunner runnerPrefab;
    [SerializeField] private int gameSceneBuildIndex = 1;

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
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button leaveFromRoomsButton;

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

    private bool isBusy;
    private bool isInLobby;
    private bool isInRoom;

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

        createRoomButton.onClick.AddListener(CreateRoom);
        leaveFromRoomsButton.onClick.AddListener(LeaveAll);

        leaveFromWaitingButton.onClick.AddListener(LeaveAll);
        startGameButton.onClick.AddListener(StartGameForAll);

        playerNameInput.onValueChanged.AddListener(_ => RefreshButtons());
        lobbyNameInput.onValueChanged.AddListener(_ => RefreshButtons());
        roomNameInput.onValueChanged.AddListener(_ => RefreshButtons());
        maxPlayersDropdown.onValueChanged.AddListener(_ => RefreshButtons());

        ClearRoomList();
        ClearPlayersList();

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

        if (createRoomButton != null)
            createRoomButton.onClick.RemoveListener(CreateRoom);

        if (leaveFromRoomsButton != null)
            leaveFromRoomsButton.onClick.RemoveListener(LeaveAll);

        if (leaveFromWaitingButton != null)
            leaveFromWaitingButton.onClick.RemoveListener(LeaveAll);

        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(StartGameForAll);
    }

    private void CreateRunner()
    {
        if (runnerPrefab == null)
        {
            Debug.LogError("Runner Prefab is not assigned.");
            return;
        }

        runner = Instantiate(runnerPrefab);
        runner.name = "NetworkRunner";

        DontDestroyOnLoad(runner.gameObject);

        sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();

        if (sceneManager == null)
            sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        runner.AddCallbacks(this);
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

        StartGameResult result = await runner.JoinSessionLobby(SessionLobby.Shared, lobbyName);

        isBusy = false;

        if (result.Ok)
        {
            currentLobbyName = lobbyName;
            isInLobby = true;
            isInRoom = false;

            SetLobbyText("Lobby: " + currentLobbyName);
            SetRoomText("Room: Not joined");

            ClearRoomList();
            ClearPlayersList();

            panelsUI.ShowRooms();

            SetStatus("Joined lobby successfully");
        }
        else
        {
            currentLobbyName = string.Empty;
            isInLobby = false;
            isInRoom = false;

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
            SetStatus("Select max players from 2 to 5");
            RefreshButtons();
            return;
        }

        isBusy = true;
        RefreshButtons();

        SetStatus("Creating room...");

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            PlayerCount = maxPlayers,
            CustomLobbyName = currentLobbyName,
            SceneManager = sceneManager
        });

        isBusy = false;

        if (result.Ok)
        {
            currentRoomName = roomName;
            isInRoom = true;

            SetRoomText("Room: " + currentRoomName);

            ClearRoomList();

            panelsUI.ShowWaiting();

            SpawnLobbyPlayerInfo();
            RequestPlayersListRefresh();

            SetStatus("Room created successfully");
        }
        else
        {
            currentRoomName = string.Empty;
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
            isInRoom = true;

            SetRoomText("Room: " + currentRoomName);

            ClearRoomList();

            panelsUI.ShowWaiting();

            SpawnLobbyPlayerInfo();
            RequestPlayersListRefresh();

            SetStatus("Joined room successfully");
        }
        else
        {
            currentRoomName = string.Empty;
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

        LobbyPlayerInfo info = playerObject.GetComponent<LobbyPlayerInfo>();

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

        SetStatus("Loading game scene...");

        runner.LoadScene(SceneRef.FromIndex(gameSceneBuildIndex), LoadSceneMode.Single);
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

        ClearRoomList();
        ClearPlayersList();

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

        return maxPlayers >= 2 && maxPlayers <= 5;
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
                                        maxPlayersIsValid;

    if (leaveFromRoomsButton != null)
        leaveFromRoomsButton.interactable = !isBusy && isInLobby && !isInRoom;

    if (leaveFromWaitingButton != null)
        leaveFromWaitingButton.interactable = !isBusy && isInRoom;

    if (startGameButton != null)
        startGameButton.interactable = !isBusy &&
                                       isInRoom &&
                                       runner != null &&
                                       runner.IsSharedModeMasterClient;
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
                LobbyPlayerInfo info = playerObject.GetComponent<LobbyPlayerInfo>();

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

    public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList)
    {
        if (!isInLobby || isInRoom || panelsUI.CurrentState != LobbyPanelState.Rooms)
            return;

        ClearRoomList();

        foreach (SessionInfo session in sessionList)
        {
            if (!session.IsVisible)
                continue;

            if (!session.IsOpen)
                continue;

            if (session.PlayerCount >= session.MaxPlayers)
                continue;

            RoomButtonUI roomButton = Instantiate(roomButtonPrefab, roomListContent);
            roomButton.Init(session, JoinRoom);
        }

        SetStatus("Room list updated");
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

        ClearRoomList();
        ClearPlayersList();

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

        ClearRoomList();
        ClearPlayersList();

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

