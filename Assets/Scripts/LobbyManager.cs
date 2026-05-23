using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Fusion")]
    [SerializeField] private NetworkRunner runnerPrefab;

    [Header("Inputs")]
    [SerializeField] private TMP_InputField lobbyNameInput;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_InputField maxPlayersInput;

    [Header("Buttons")]
    [SerializeField] private Button joinLobbyButton;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button leaveButton;

    [Header("Room List")]
    [SerializeField] private Transform roomListParent;
    [SerializeField] private RoomButtonUI roomButtonPrefab;

    [Header("Texts")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text lobbyText;
    [SerializeField] private TMP_Text roomText;
    [SerializeField] private TMP_Text playersText;

    private NetworkRunner runner;
    private string currentLobbyName;
    private bool isInLobby;
    private bool isInRoom;

    private void Awake()
    {
        CreateRunner();
        
        joinLobbyButton.onClick.AddListener(JoinLobby);
        createRoomButton.onClick.AddListener(CreateRoom);
        leaveButton.onClick.AddListener(Leave);

        RefreshButtons();
        RefreshPlayersText();

        statusText.text = "Ready";
        lobbyText.text = "Lobby: Not joined";
        roomText.text = "Room: Not joined";
    }

    private void OnDestroy()
    {
        if (runner)
        {
            runner.RemoveCallbacks(this);
        }

        joinLobbyButton.onClick.RemoveListener(JoinLobby);
        createRoomButton.onClick.RemoveListener(CreateRoom);
        leaveButton.onClick.RemoveListener(Leave);
    }

    private void CreateRunner()
    {
        runner = Instantiate(runnerPrefab);
        runner.name = "NetworkRunner";
        runner.AddCallbacks(this);
    }
    private async void JoinLobby()
    {
        currentLobbyName = lobbyNameInput.text;

        if (string.IsNullOrWhiteSpace(currentLobbyName))
        {
            statusText.text = "Enter lobby name";
            return;
        }

        statusText.text = "Joining lobby...";

        StartGameResult result = await runner.JoinSessionLobby(SessionLobby.Shared, currentLobbyName);

        if (result.Ok)
        {
            isInLobby = true;
            lobbyText.text = "Lobby: " + currentLobbyName;
            statusText.text = "Joined lobby successfully";
        }
        else
        {
            isInLobby = false;
            statusText.text = "Failed to join lobby: " + result.ShutdownReason;
        }

        RefreshButtons();
    }

    private async void CreateRoom()
    {
        if (!isInLobby)
        {
            statusText.text = "Join lobby first";
            return;
        }

        string roomName = roomNameInput.text;

        if (string.IsNullOrWhiteSpace(roomName))
        {
            statusText.text = "Enter room name";
            return;
        }

        int maxPlayers = 2;
        int.TryParse(maxPlayersInput.text, out maxPlayers);

        if (maxPlayers < 2)
        {
            maxPlayers = 2;
        }

        statusText.text = "Creating room...";

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            PlayerCount = maxPlayers,
            CustomLobbyName = currentLobbyName
        });

        if (result.Ok)
        {
            isInRoom = true;
            roomText.text = "Room: " + roomName;
            statusText.text = "Room created / joined successfully";
            RefreshPlayersText();
        }
        else
        {
            isInRoom = false;
            statusText.text = "Failed to create room: " + result.ShutdownReason;
        }

        RefreshButtons();
    }

    private async void JoinRoom(SessionInfo session)
    {
        statusText.text = "Joining room...";

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = session.Name,
            CustomLobbyName = currentLobbyName
        });

        if (result.Ok)
        {
            isInRoom = true;
            roomText.text = "Room: " + session.Name;
            statusText.text = "Joined room successfully";
            RefreshPlayersText();
        }
        else
        {
            isInRoom = false;
            statusText.text = "Failed to join room: " + result.ShutdownReason;
        }

        RefreshButtons();
    }

    private async void Leave()
    {
        statusText.text = "Leaving...";

        if (runner)
        {
            await runner.Shutdown();
            runner = null;
        }

        isInLobby = false;
        isInRoom = false;

        lobbyText.text = "Lobby: Not joined";
        roomText.text = "Room: Not joined";

        ClearRoomList();
        RefreshPlayersText();

        CreateRunner();

        RefreshButtons();

        statusText.text = "Left. You can join a lobby again.";
    }

    private void RefreshButtons()
    {
        joinLobbyButton.interactable = !isInRoom;
        createRoomButton.interactable = isInLobby && !isInRoom;
        leaveButton.interactable = isInRoom;
    }

    private void RefreshPlayersText()
    {
        if (!isInRoom)
        {
            playersText.text = "Players: -";
            return;
        }

        string text = "Players:\n";

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (player == runner.LocalPlayer)
            {
                text += $"Player {player.PlayerId} (You)\n";
            }
            else
            {
                text += $"Player {player.PlayerId}\n";
            }
        }

        playersText.text = text;
    }

    private void ClearRoomList()
    {
        foreach (Transform child in roomListParent)
        {
            Destroy(child.gameObject);
        }
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        ClearRoomList();

        foreach (SessionInfo session in sessionList)
        {
            RoomButtonUI roomButton = Instantiate(roomButtonPrefab, roomListParent);
            roomButton.Init(session, JoinRoom);
        }

        statusText.text = "Room list updated";
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        statusText.text = "Player joined: " + player.PlayerId;
        RefreshPlayersText();
        RefreshButtons();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        statusText.text = "Player left: " + player.PlayerId;
        RefreshPlayersText();
        RefreshButtons();
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        isInLobby = false;
        isInRoom = false;

        lobbyText.text = "Lobby: Not joined";
        roomText.text = "Room: Not joined";
        statusText.text = "Shutdown: " + shutdownReason;

        ClearRoomList();
        RefreshPlayersText();
        RefreshButtons();
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        statusText.text = "Connected to server";
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        statusText.text = "Connection failed: " + reason;
        RefreshButtons();
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        statusText.text = "Disconnected: " + reason;

        isInLobby = false;
        isInRoom = false;

        RefreshPlayersText();
        RefreshButtons();
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}

