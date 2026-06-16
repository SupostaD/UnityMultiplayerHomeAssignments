using System;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameSceneManager : NetworkBehaviour, INetworkRunnerCallbacks
{
    [Header("Player")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    [Header("Character Selection")]
    [SerializeField] private CharacterSelectionUI characterSelectionUI;
    [SerializeField] private Color[] characterColors = new Color[10];

    [Header("End Game")]
    [SerializeField] private Button endGameButton;
    [SerializeField] private EndGameUI endGameUI;
    [SerializeField] private GamePauseUI gamePauseUI;
    [SerializeField] private GameObject endGamePanel;
    [SerializeField] private TMP_Text endGameMessageText;
    [SerializeField] private int mainMenuSceneBuildIndex = 0;

    [Networked, OnChangedRender(nameof(OnOccupiedCharactersChanged))]
    private int OccupiedCharactersMask { get; set; }

    [Networked]
    private PlayerRef OriginalHostPlayer { get; set; }

    public static GameSceneManager Instance { get; private set; }

    private const int MaxCharacters = 10;
    private const int ShutdownTimeoutMilliseconds = 2000;

    private readonly PlayerRef[] characterOwners = new PlayerRef[MaxCharacters];

    private NetworkRunner runner;
    private NetworkObject localPlayerObject;
    private PlayerRef localOriginalHostPlayer = PlayerRef.None;

    private bool endGamePanelShown;
    private bool gameEnded;
    private bool isLeavingGameIntentionally;

    public bool IsGameEnded => gameEnded;

    private void Awake()
    {
        Instance = this;

        for (int i = 0; i < characterOwners.Length; i++)
            characterOwners[i] = PlayerRef.None;

        if (endGameButton != null)
            endGameButton.onClick.AddListener(RequestEndGame);

        if (gamePauseUI == null)
            gamePauseUI = FindAnyObjectByType<GamePauseUI>();
    }

    private void OnDestroy()
    {
        if (endGameButton != null)
            endGameButton.onClick.RemoveListener(RequestEndGame);

        if (Instance == this)
            Instance = null;
    }

    public override void Spawned()
    {
        runner = Runner;
        runner.AddCallbacks(this);

        RememberOriginalHost();

        if (Object.HasStateAuthority)
        {
            OccupiedCharactersMask = 0;

            if (OriginalHostPlayer == PlayerRef.None && localOriginalHostPlayer != PlayerRef.None)
                OriginalHostPlayer = localOriginalHostPlayer;
        }

        HideEndGamePanel();
        RefreshEndGameButton();

        if (characterSelectionUI != null)
        {
            characterSelectionUI.Init(characterColors);
            characterSelectionUI.Refresh(OccupiedCharactersMask);
            characterSelectionUI.Show();
            characterSelectionUI.SetStatus("Choose a character");
        }
    }

    public override void Despawned(NetworkRunner despawnRunner, bool hasState)
    {
        if (despawnRunner != null)
            despawnRunner.RemoveCallbacks(this);
    }

    private void Update()
    {
        RefreshEndGameButton();
        CheckOriginalHostStillInGame();
    }

    private void RefreshEndGameButton()
    {
        bool canEndGame =
            runner != null &&
            runner.IsSharedModeMasterClient &&
            !gameEnded;

        if (endGameButton != null)
        {
            endGameButton.gameObject.SetActive(canEndGame);
            endGameButton.interactable = canEndGame;
        }
    }

    public void RequestEndGame()
    {
        if (gameEnded)
            return;

        if (runner == null)
            runner = Runner;

        if (runner == null || !runner.IsSharedModeMasterClient)
        {
            Debug.Log("Only MasterClient can end the game.");
            return;
        }

        string masterName = "MasterClient";

        if (GameChatNetwork.Instance != null)
            masterName = GameChatNetwork.Instance.GetPlayerName(runner.LocalPlayer);

        RPC_ShowEndGamePanel($"Game ended by {masterName}.\nPress Exit to return to main menu.");
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ShowEndGamePanel(NetworkString<_128> message)
    {
        ShowEndGamePanel(message.ToString());
    }

    private void ShowEndGamePanel(string message)
    {
        if (endGamePanelShown)
            return;

        endGamePanelShown = true;
        gameEnded = true;

        GameInputBlocker.BlockGameplayInput();

        gamePauseUI?.CloseForGameEnd();
        characterSelectionUI?.Hide();

        if (endGameButton != null)
            endGameButton.gameObject.SetActive(false);

        if (endGameUI != null)
        {
            endGameUI.Show(message);
        }
        else if (endGamePanel != null)
        {
            endGamePanel.SetActive(true);

            if (endGameMessageText != null)
                endGameMessageText.text = message;
        }
    }

    private void HideEndGamePanel()
    {
        endGamePanelShown = false;
        gameEnded = false;

        if (endGameUI != null)
            endGameUI.Hide();
        else if (endGamePanel != null)
            endGamePanel.SetActive(false);
    }

    private void RememberOriginalHost()
    {
        if (runner == null || localOriginalHostPlayer != PlayerRef.None)
            return;

        localOriginalHostPlayer = GetCurrentMasterClient();

        if (Object != null &&
            Object.HasStateAuthority &&
            OriginalHostPlayer == PlayerRef.None &&
            localOriginalHostPlayer != PlayerRef.None)
        {
            OriginalHostPlayer = localOriginalHostPlayer;
        }
    }

    private PlayerRef GetOriginalHostPlayer()
    {
        if (OriginalHostPlayer != PlayerRef.None)
            return OriginalHostPlayer;

        return localOriginalHostPlayer;
    }

    private PlayerRef GetCurrentMasterClient()
    {
        if (runner == null)
            return PlayerRef.None;

        try
        {
            return runner.GetMasterClient();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Unable to get current MasterClient: " + exception.Message);
            return PlayerRef.None;
        }
    }

    private void CheckOriginalHostStillInGame()
    {
        if (runner == null || gameEnded || isLeavingGameIntentionally)
            return;

        RememberOriginalHost();

        PlayerRef originalHost = GetOriginalHostPlayer();

        if (originalHost == PlayerRef.None)
            return;

        if (!IsPlayerActive(originalHost))
        {
            EndGameBecauseHostLeft();
            return;
        }

        PlayerRef currentMasterClient = GetCurrentMasterClient();

        if (currentMasterClient != PlayerRef.None && currentMasterClient != originalHost)
            EndGameBecauseHostLeft();
    }

    private bool IsPlayerActive(PlayerRef player)
    {
        if (runner == null)
            return false;

        foreach (PlayerRef activePlayer in runner.ActivePlayers)
        {
            if (activePlayer == player)
                return true;
        }

        return false;
    }

    private void EndGameBecauseHostLeft()
    {
        ShowEndGamePanel("Host left the game.\nGame is over.");
    }

    public async void LeaveCurrentGame()
    {
        if (isLeavingGameIntentionally)
            return;

        isLeavingGameIntentionally = true;

        try
        {
            if (runner == null)
                runner = Runner;

            NetworkRunner runnerToShutdown = runner;
            runner = null;

            if (runnerToShutdown != null)
            {
                PlayerRef originalHost = GetOriginalHostPlayer();
                bool localPlayerIsOriginalHost =
                    originalHost != PlayerRef.None &&
                    runnerToShutdown.LocalPlayer == originalHost;

                if (localPlayerIsOriginalHost && !gameEnded)
                {
                    RPC_ShowEndGamePanel("Host left the game.\nGame is over.");
                    await Task.Delay(250);
                }

                runnerToShutdown.RemoveCallbacks(this);

                Task shutdownTask = runnerToShutdown.Shutdown();
                Task finishedTask = await Task.WhenAny(
                    shutdownTask,
                    Task.Delay(ShutdownTimeoutMilliseconds)
                );

                if (finishedTask == shutdownTask)
                {
                    await shutdownTask;
                }
                else
                {
                    Debug.LogWarning("NetworkRunner shutdown timed out. Returning to main menu anyway.");
                }

                if (runnerToShutdown != null)
                    Destroy(runnerToShutdown.gameObject);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Failed to leave current game cleanly: " + exception.Message);
        }
        finally
        {
            GameInputBlocker.UnblockGameplayInput();
            SceneManager.LoadScene(mainMenuSceneBuildIndex);
        }
    }

    public Color GetCharacterColor(int characterIndex)
    {
        if (characterColors == null)
            return Color.white;

        if (characterIndex < 0 || characterIndex >= characterColors.Length)
            return Color.white;

        return characterColors[characterIndex];
    }

    public void RequestCharacter(int characterIndex)
    {
        if (!IsValidCharacterIndex(characterIndex))
        {
            characterSelectionUI?.SetStatus("Invalid character");
            return;
        }

        if (localPlayerObject != null)
        {
            characterSelectionUI?.SetStatus("You already spawned");
            return;
        }

        characterSelectionUI?.SetStatus("Requesting character...");
        RPC_RequestCharacter(characterIndex);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestCharacter(int characterIndex, RpcInfo info = default)
    {
        PlayerRef requestingPlayer = info.Source;

        if (!IsValidCharacterIndex(characterIndex))
        {
            RPC_CharacterRejected(requestingPlayer, "Invalid character");
            return;
        }

        if (PlayerAlreadyHasCharacter(requestingPlayer))
        {
            RPC_CharacterRejected(requestingPlayer, "You already selected a character");
            return;
        }

        if (IsCharacterOccupied(characterIndex))
        {
            RPC_CharacterRejected(requestingPlayer, "This character is already occupied. Choose another one.");
            return;
        }

        characterOwners[characterIndex] = requestingPlayer;
        OccupiedCharactersMask |= 1 << characterIndex;

        Vector3 spawnPosition = GetSpawnPosition(characterIndex);
        Quaternion spawnRotation = GetSpawnRotation(characterIndex);

        RPC_CharacterApproved(
            requestingPlayer,
            characterIndex,
            spawnPosition,
            spawnRotation.eulerAngles.y
        );
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_CharacterApproved(
        [RpcTarget] PlayerRef targetPlayer,
        int characterIndex,
        Vector3 spawnPosition,
        float spawnYRotation)
    {
        if (runner == null)
            runner = Runner;

        if (runner.LocalPlayer != targetPlayer)
            return;

        SpawnLocalPlayer(characterIndex, spawnPosition, spawnYRotation);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_CharacterRejected(
        [RpcTarget] PlayerRef targetPlayer,
        string reason)
    {
        if (runner == null)
            runner = Runner;

        if (runner.LocalPlayer != targetPlayer)
            return;

        characterSelectionUI?.SetStatus(reason);
        characterSelectionUI?.Show();
    }

    private void SpawnLocalPlayer(int characterIndex, Vector3 spawnPosition, float spawnYRotation)
    {
        if (runner == null)
            runner = Runner;

        if (playerPrefab == null)
        {
            Debug.LogError("Player Prefab is not assigned.");
            return;
        }

        if (localPlayerObject != null)
            return;

        Quaternion spawnRotation = Quaternion.Euler(0f, spawnYRotation, 0f);

        localPlayerObject = runner.Spawn(
            playerPrefab,
            spawnPosition,
            spawnRotation,
            runner.LocalPlayer
        );

        NetworkPlayerCharacter playerCharacter =
            localPlayerObject.GetComponent<NetworkPlayerCharacter>();

        if (playerCharacter != null)
            playerCharacter.CharacterIndex = characterIndex;

        runner.SetPlayerObject(runner.LocalPlayer, localPlayerObject);

        characterSelectionUI?.Hide();
    }

    private bool PlayerAlreadyHasCharacter(PlayerRef player)
    {
        for (int i = 0; i < characterOwners.Length; i++)
        {
            if (characterOwners[i] == player)
                return true;
        }

        return false;
    }

    private bool IsCharacterOccupied(int characterIndex)
    {
        return (OccupiedCharactersMask & (1 << characterIndex)) != 0;
    }

    private bool IsValidCharacterIndex(int characterIndex)
    {
        return characterIndex >= 0 && characterIndex < MaxCharacters;
    }

    private Vector3 GetSpawnPosition(int characterIndex)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return Vector3.zero;

        int spawnIndex = characterIndex % spawnPoints.Length;
        return spawnPoints[spawnIndex].position;
    }

    private Quaternion GetSpawnRotation(int characterIndex)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return Quaternion.identity;

        int spawnIndex = characterIndex % spawnPoints.Length;
        return spawnPoints[spawnIndex].rotation;
    }

    private void OnOccupiedCharactersChanged()
    {
        characterSelectionUI?.Refresh(OccupiedCharactersMask);
    }

    public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player)
    {
        PlayerRef originalHost = GetOriginalHostPlayer();
        bool originalHostLeft =
            originalHost != PlayerRef.None &&
            player == originalHost;

        if (originalHostLeft && !isLeavingGameIntentionally)
            EndGameBecauseHostLeft();

        if (!Object.HasStateAuthority)
            return;

        bool changed = false;

        for (int i = 0; i < characterOwners.Length; i++)
        {
            if (characterOwners[i] != player)
                continue;

            characterOwners[i] = PlayerRef.None;
            OccupiedCharactersMask &= ~(1 << i);
            changed = true;
        }

        if (changed)
            Debug.Log("Released character of player: " + player.PlayerId);
    }

    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player) { }
    public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner callbackRunner) { }
    public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner callbackRunner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner callbackRunner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner callbackRunner) { }
    public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
    public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, float progress) { }
}
