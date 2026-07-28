using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameSceneManager :
    NetworkBehaviour,
    INetworkRunnerCallbacks,
    IStateAuthorityChanged
{
    private static readonly Color[] DefaultCharacterColors =
    {
        Color.red,
        Color.blue,
        Color.green,
        Color.yellow,
        Color.cyan,
        Color.magenta,
        new Color(1f, 0.5f, 0f),
        new Color(0.5f, 0f, 1f),
        new Color(0.1f, 0.1f, 0.1f),
        Color.white
    };

    [Header("Player")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    [Header("Hex Map Spawn")]
    [SerializeField] private HexMapGenerator hexMapGenerator;
    [SerializeField] private HexTerritoryManager hexTerritoryManager;
    [SerializeField] private HexStrengthCombatManager hexStrengthCombatManager;
    [SerializeField] private bool spawnOnHexMapCorners = true;
    [SerializeField, Min(0f)] private float spawnHeightAboveTile = 1.6f;
    [SerializeField] private HexCoord[] islandSpawnCoordinates =
        new HexCoord[4];

    [Header("Game Settings")]
    [SerializeField] private HexPlayerGrowthSettings playerGrowthSettings;
    [SerializeField] private HexGameRulesSettings gameRulesSettings;
    [SerializeField] private HexPlayerCollisionSettings playerCollisionSettings;

    [Header("Character Selection")] [SerializeField]
    private CharacterSelectionUI characterSelectionUI;

    [SerializeField] private Color[] characterColors =
    {
        Color.red,
        Color.blue,
        Color.green,
        Color.yellow,
        Color.cyan,
        Color.magenta,
        new Color(1f, 0.5f, 0f),
        new Color(0.5f, 0f, 1f),
        new Color(0.1f, 0.1f, 0.1f),
        Color.white
    };
    
    [Header("Avatar Selection")]
    [SerializeField] private AvatarSelectionManager avatarSelectionManager;

    [Header("End Game")] [SerializeField] private Button endGameButton;
    [SerializeField] private EndGameUI endGameUI;
    [SerializeField] private GamePauseUI gamePauseUI;
    [SerializeField] private GameObject endGamePanel;
    [SerializeField] private TMP_Text endGameMessageText;
    [SerializeField] private TMP_Text matchTimerText;
    [SerializeField] private int mainMenuSceneBuildIndex = 0;

    [Networked, OnChangedRender(nameof(OnOccupiedCharactersChanged))]
    private int OccupiedCharactersMask { get; set; }

    [Networked, OnChangedRender(nameof(OnMatchStartedChanged))]
    private NetworkBool MatchStarted { get; set; }

    [Networked]
    private TickTimer MatchTimer { get; set; }

    [Networked]
    private NetworkString<_512> FinalResults { get; set; }

    [Networked, OnChangedRender(nameof(OnMatchEndedChanged))]
    private NetworkBool MatchEnded { get; set; }

    [Networked, Capacity(MaxCharacters)]
    private NetworkArray<PlayerRef> CharacterOwners => default;

    public static GameSceneManager Instance { get; private set; }

    private const int MinimumPlayersToStart = 2;
    private const int MaxCharacters = 10;
    private const int CornerSpawnCount = 6;
    private const int IslandSpawnCount = MaxCharacters - CornerSpawnCount;
    private const int ShutdownTimeoutMilliseconds = 2000;

    private NetworkRunner runner;
    private NetworkObject localPlayerObject;
    private int localCharacterIndex = -1;
    private int localSpawnIndex = -1;

    private bool endGamePanelShown;
    private bool gameEnded;
    private bool isLeavingGameIntentionally;

    public bool IsGameEnded => gameEnded || MatchEnded;
    public bool IsMatchStarted => MatchStarted;
    public HexTerritoryManager HexTerritory => hexTerritoryManager;
    public HexStrengthCombatManager HexStrengthCombat => hexStrengthCombatManager;
    public HexPlayerGrowthSettings PlayerGrowthSettings => playerGrowthSettings;
    public HexGameRulesSettings GameRulesSettings => gameRulesSettings;
    public HexPlayerCollisionSettings PlayerCollisionSettings =>
        playerCollisionSettings;
    public NetworkRunner ActiveRunner =>
        runner != null ? runner : Runner;

    private void Awake()
    {
        Instance = this;

        if (hexTerritoryManager == null)
            Debug.LogError(
                "GameSceneManager: Hex Territory Manager is not assigned.",
                this
            );

        if (hexStrengthCombatManager == null)
            Debug.LogError(
                "GameSceneManager: Hex Strength Combat Manager is not assigned.",
                this
            );

        if (playerGrowthSettings == null)
            Debug.LogError(
                "GameSceneManager: Player Growth Settings is not assigned.",
                this
            );

        if (gameRulesSettings == null)
            Debug.LogError(
                "GameSceneManager: Game Rules Settings is not assigned.",
                this
            );

        if (playerCollisionSettings == null)
            Debug.LogError(
                "GameSceneManager: Player Collision Settings is not assigned.",
                this
            );

        if (islandSpawnCoordinates == null ||
            islandSpawnCoordinates.Length != IslandSpawnCount)
        {
            Debug.LogError(
                $"GameSceneManager: Island Spawn Coordinates must contain exactly {IslandSpawnCount} coordinates.",
                this
            );
        }

        if (endGameUI != null &&
            endGameButton ==
            endGameUI.ExitToMainMenuButton)
        {
            Debug.LogError(
                "GameSceneManager: End Game Button and Exit To Main Menu Button must be different buttons.",
                this
            );
            endGameButton = null;
        }

        if (endGameButton != null)
            endGameButton.onClick.AddListener(RequestEndGame);

    }

    private void OnDestroy()
    {
        if (endGameButton != null)
            endGameButton.onClick.RemoveListener(RequestEndGame);

        if (runner != null)
            runner.RemoveCallbacks(this);

        if (Instance == this)
            Instance = null;
    }

    public override void Spawned()
    {
        runner = Runner;

        if (runner != null)
            runner.AddCallbacks(this);

        if (Object.HasStateAuthority)
        {
            OccupiedCharactersMask = 0;
            MatchStarted = false;
            MatchEnded = false;
            MatchTimer = default;
            FinalResults = default;

            for (int i = 0; i < MaxCharacters; i++)
                CharacterOwners.Set(i, PlayerRef.None);
        }

        HideEndGamePanel();
        RefreshEndGameButton();

        if (characterSelectionUI != null)
        {
            characterSelectionUI.Init(GetSelectableCharacterColors());
            characterSelectionUI.Refresh(OccupiedCharactersMask);
            characterSelectionUI.Show();
            characterSelectionUI.SetStatus("Choose a character");
        }

        avatarSelectionManager?.HideSelectionUI();

        if (MatchEnded)
            ShowEndGamePanel(FinalResults.ToString());
    }

    public override void Despawned(NetworkRunner despawnRunner, bool hasState)
    {
        if (despawnRunner != null)
            despawnRunner.RemoveCallbacks(this);
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null ||
            !Object.HasStateAuthority ||
            Runner == null ||
            MatchEnded)
        {
            return;
        }

        if (!MatchStarted)
        {
            TryStartMatchWhenAllPlayersSelected();
            return;
        }

        if (MatchTimer.Expired(Runner))
            EndMatchInternal(false);
    }

    public override void Render()
    {
        if (MatchEnded && !endGamePanelShown)
            ShowEndGamePanel(FinalResults.ToString());
    }

    private void Update()
    {
        RefreshEndGameButton();
        RefreshMatchTimerText();
    }

    private void RefreshEndGameButton()
    {
        bool canEndGame =
            runner != null &&
            runner.IsSharedModeMasterClient &&
            !IsGameEnded;

        if (endGameButton != null)
        {
            endGameButton.gameObject.SetActive(canEndGame);
            endGameButton.interactable = canEndGame;
        }
    }

    public void RequestEndGame()
    {
        if (IsGameEnded)
            return;

        if (runner == null)
            runner = Runner;

        if (runner == null || !runner.IsSharedModeMasterClient)
        {
            Debug.Log("Only MasterClient can end the game.");
            return;
        }

        if (Object != null && Object.HasStateAuthority)
        {
            EndMatchInternal(true);
            return;
        }

        RPC_RequestManualEndGame();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestManualEndGame(
        RpcInfo info = default)
    {
        if (Runner == null ||
            !Runner.IsSharedModeMasterClient ||
            info.Source != Runner.LocalPlayer)
        {
            return;
        }

        EndMatchInternal(true);
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

        if (matchTimerText != null)
            matchTimerText.gameObject.SetActive(false);

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

        RefreshMatchTimerText();
    }

    private void EndMatchInternal(bool endedManually)
    {
        if (Object == null ||
            !Object.HasStateAuthority ||
            MatchEnded)
        {
            return;
        }

        string results =
            BuildFinalResults(endedManually);

        FinalResults = results;
        MatchTimer = default;
        MatchEnded = true;

        ShowEndGamePanel(results);
    }

    private void OnMatchEndedChanged()
    {
        if (!MatchEnded)
            return;

        ShowEndGamePanel(FinalResults.ToString());
    }

    private void RefreshMatchTimerText()
    {
        if (matchTimerText == null)
            return;

        bool shouldShow =
            Runner != null &&
            MatchStarted &&
            !IsGameEnded;

        matchTimerText.gameObject.SetActive(shouldShow);

        if (!shouldShow)
            return;

        float remainingSeconds =
            MatchTimer.RemainingTime(Runner) ?? 0f;
        int displayedSeconds =
            Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
        int minutes = displayedSeconds / 60;
        int seconds = displayedSeconds % 60;

        matchTimerText.text =
            $"{minutes}:{seconds:00}";
    }

    private string BuildFinalResults(bool endedManually)
    {
        List<MatchResultEntry> results =
            new List<MatchResultEntry>();

        if (Runner != null)
        {
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                results.Add(
                    new MatchResultEntry
                    {
                        Player = player,
                        PlayerName = GetShortPlayerName(player),
                        Strength =
                            hexTerritoryManager != null
                                ? hexTerritoryManager
                                    .GetDisplayedStrength(player)
                                : 0,
                        IsDead =
                            hexTerritoryManager != null &&
                            hexTerritoryManager
                                .IsPlayerEliminated(player)
                    }
                );
            }
        }

        results.Sort(CompareMatchResults);

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("MATCH RESULTS");
        builder.AppendLine();

        int aliveCount = 0;

        while (aliveCount < results.Count &&
               !results[aliveCount].IsDead)
        {
            aliveCount++;
        }

        int resultIndex = 0;

        while (resultIndex < aliveCount)
        {
            int groupEnd = resultIndex + 1;
            int groupStrength =
                results[resultIndex].Strength;

            while (groupEnd < aliveCount &&
                   results[groupEnd].Strength ==
                   groupStrength)
            {
                groupEnd++;
            }

            bool isDraw = groupEnd - resultIndex > 1;
            int place = resultIndex + 1;

            for (int i = resultIndex; i < groupEnd; i++)
            {
                builder.Append(place);
                builder.Append(". ");
                builder.Append(results[i].PlayerName);
                builder.Append(" - ");
                builder.Append(results[i].Strength);
                builder.Append(" STR");

                if (isDraw)
                    builder.Append(" - DRAW");

                builder.AppendLine();
            }

            resultIndex = groupEnd;
        }

        for (int i = aliveCount; i < results.Count; i++)
        {
            builder.Append("Last. ");
            builder.Append(results[i].PlayerName);
            builder.AppendLine(" - DEAD");
        }

        builder.AppendLine();
        AppendWinnerSummary(
            builder,
            results,
            aliveCount
        );
        builder.AppendLine(
            endedManually
                ? "Ended by MasterClient"
                : "Time is over"
        );

        return builder.ToString();
    }

    private static int CompareMatchResults(
        MatchResultEntry first,
        MatchResultEntry second)
    {
        if (first.IsDead != second.IsDead)
            return first.IsDead ? 1 : -1;

        int strengthComparison =
            second.Strength.CompareTo(first.Strength);

        if (strengthComparison != 0)
            return strengthComparison;

        return first.Player.PlayerId.CompareTo(
            second.Player.PlayerId
        );
    }

    private static void AppendWinnerSummary(
        StringBuilder builder,
        List<MatchResultEntry> results,
        int aliveCount)
    {
        if (aliveCount <= 0)
        {
            builder.AppendLine(
                "Winner: nobody - all players are dead"
            );
            return;
        }

        int bestStrength = results[0].Strength;
        int winnerCount = 1;

        while (winnerCount < aliveCount &&
               results[winnerCount].Strength ==
               bestStrength)
        {
            winnerCount++;
        }

        if (winnerCount == 1)
        {
            builder.Append("Winner: ");
            builder.AppendLine(results[0].PlayerName);
            return;
        }

        builder.AppendLine(
            "Winners: DRAW at first place"
        );
    }

    private static string GetShortPlayerName(
        PlayerRef player)
    {
        string playerName =
            GameChatNetwork.Instance != null
                ? GameChatNetwork.Instance.GetPlayerName(player)
                : "Player " + player.PlayerId;

        if (string.IsNullOrWhiteSpace(playerName))
            playerName = "Player " + player.PlayerId;

        playerName = playerName
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        return playerName.Length > 16
            ? playerName.Substring(0, 16)
            : playerName;
    }

    private struct MatchResultEntry
    {
        public PlayerRef Player;
        public string PlayerName;
        public int Strength;
        public bool IsDead;
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
        if (characterIndex < 0 ||
            characterIndex >= MaxCharacters)
            return Color.white;

        return GetConfiguredCharacterColor(characterIndex);
    }

    public void RequestCharacter(int characterIndex)
    {
        if (!IsValidCharacterIndex(characterIndex))
        {
            characterSelectionUI?.SetStatus("Invalid character");
            return;
        }

        if (runner == null)
            runner = Runner;

        if (localCharacterIndex >= 0)
        {
            characterSelectionUI?.SetStatus(
                "You already selected a color"
            );
            return;
        }

        if (localPlayerObject != null)
        {
            characterSelectionUI?.SetStatus("You already spawned");
            return;
        }

        if (runner != null)
        {
            NetworkObject currentPlayerObject = runner.GetPlayerObject(runner.LocalPlayer);

            if (NetworkObjectBehaviourReferences.TryGet(
                    currentPlayerObject,
                    out HexBallPlayerController _))
            {
                localPlayerObject = currentPlayerObject;
                characterSelectionUI?.SetStatus("You already spawned");
                return;
            }
        }

        characterSelectionUI?.SetStatus("Requesting character...");
        RPC_RequestCharacter(characterIndex);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestCharacter(int characterIndex, RpcInfo info = default)
    {
        PlayerRef requestingPlayer = info.Source;

        if (MatchStarted)
        {
            RPC_CharacterRejected(
                requestingPlayer,
                "The match has already started"
            );
            return;
        }

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

        CharacterOwners.Set(characterIndex, requestingPlayer);
        OccupiedCharactersMask |= 1 << characterIndex;

        int spawnIndex = GetSpawnIndexForCharacter(characterIndex);

        Debug.Log(
            "Approved character " +
            characterIndex +
            " for player " +
            requestingPlayer +
            " using spawn index " +
            spawnIndex
        );

        RPC_CharacterApproved(
            requestingPlayer,
            characterIndex,
            spawnIndex
        );

        TryStartMatchWhenAllPlayersSelected();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_CharacterApproved(
        [RpcTarget] PlayerRef targetPlayer,
        int characterIndex,
        int spawnIndex)
    {
        if (runner == null)
            runner = Runner;

        if (runner == null)
            return;

        if (runner.LocalPlayer != targetPlayer)
            return;

        localCharacterIndex = characterIndex;
        localSpawnIndex = spawnIndex;

        characterSelectionUI?.SetStatus(
            "Color selected. Choose an avatar."
        );
        characterSelectionUI?.Hide();
        avatarSelectionManager?.ShowSelectionUI();

        TrySpawnLocalPlayerAfterSetup();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_CharacterRejected(
        [RpcTarget] PlayerRef targetPlayer,
        string reason)
    {
        if (runner == null)
            runner = Runner;

        if (runner == null)
            return;

        if (runner.LocalPlayer != targetPlayer)
            return;

        characterSelectionUI?.SetStatus(reason);
        characterSelectionUI?.Show();
    }

    private void SpawnLocalPlayer(int characterIndex, int spawnIndex)
    {
        if (runner == null)
            runner = Runner;

        if (runner == null)
        {
            Debug.LogError("Runner is missing.");
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("Player Prefab is not assigned.");
            return;
        }

        if (localPlayerObject != null)
            return;

        if (avatarSelectionManager == null ||
            avatarSelectionManager.LocalAvatarIndex < 0)
        {
            Debug.LogError(
                "GameSceneManager: Player cannot spawn before selecting an avatar.",
                this
            );
            return;
        }

        NetworkObject currentPlayerObject = runner.GetPlayerObject(runner.LocalPlayer);

        if (NetworkObjectBehaviourReferences.TryGet(
                currentPlayerObject,
                out HexBallPlayerController _))
        {
            localPlayerObject = currentPlayerObject;
            RefreshSelectionUIAfterSpawn();
            return;
        }

        Vector3 spawnPosition = GetSpawnPositionByIndex(spawnIndex);
        Quaternion spawnRotation = GetSpawnRotationByIndex(spawnIndex);

        Debug.Log(
            "Spawning local player " +
            runner.LocalPlayer +
            " | character " +
            characterIndex +
            " | spawn index " +
            spawnIndex +
            " | position " +
            spawnPosition +
            " | rotation " +
            spawnRotation.eulerAngles
        );

        int avatarIndex =
            avatarSelectionManager.LocalAvatarIndex;

        localPlayerObject = runner.Spawn(
            playerPrefab,
            spawnPosition,
            spawnRotation,
            runner.LocalPlayer,
            (_, spawnedObject) =>
            {
                ApplySpawnTransform(
                    spawnedObject,
                    spawnPosition,
                    spawnRotation
                );
                ApplyPlayerSelection(
                    spawnedObject,
                    characterIndex,
                    avatarIndex
                );
            }
        );

        if (localPlayerObject == null)
        {
            Debug.LogError("Runner.Spawn returned null.");
            return;
        }

        ApplySpawnTransform(localPlayerObject, spawnPosition, spawnRotation);

        Debug.Log(
            "Player spawned. Actual position after force set: " +
            localPlayerObject.transform.position
        );

        ApplyPlayerSelection(
            localPlayerObject,
            characterIndex,
            avatarIndex
        );

        runner.SetPlayerObject(runner.LocalPlayer, localPlayerObject);

        RefreshSelectionUIAfterSpawn();
    }

    private void RefreshSelectionUIAfterSpawn()
    {
        characterSelectionUI?.Hide();
        avatarSelectionManager?.HideSelectionUI();
    }

    public void NotifyLocalAvatarSelectionApproved()
    {
        TrySpawnLocalPlayerAfterSetup();
    }

    private void TrySpawnLocalPlayerAfterSetup()
    {
        if (localPlayerObject != null ||
            localCharacterIndex < 0 ||
            localSpawnIndex < 0 ||
            avatarSelectionManager == null ||
            avatarSelectionManager.LocalAvatarIndex < 0)
        {
            return;
        }

        characterSelectionUI?.Hide();
        avatarSelectionManager.HideSelectionUI();

        SpawnLocalPlayer(
            localCharacterIndex,
            localSpawnIndex
        );
    }

    private void ApplyPlayerSelection(
        NetworkObject playerObject,
        int characterIndex,
        int avatarIndex)
    {
        NetworkPlayerCharacter playerCharacter =
            NetworkObjectBehaviourReferences.GetRequired<NetworkPlayerCharacter>(
                playerObject,
                this
            );

        if (playerCharacter == null)
            return;

        playerCharacter.CharacterIndex = characterIndex;
        playerCharacter.AvatarIndex = avatarIndex;
    }

    private void ApplySpawnTransform(NetworkObject playerObject, Vector3 spawnPosition, Quaternion spawnRotation)
    {
        if (playerObject == null)
            return;

        HexBallPlayerController ballPlayerController =
            NetworkObjectBehaviourReferences.GetRequired<HexBallPlayerController>(
                playerObject,
                this
            );

        if (ballPlayerController != null)
        {
            ballPlayerController.InitializeSpawnTransform(spawnPosition, spawnRotation);
            return;
        }

        NetworkTransform networkTransform =
            NetworkObjectBehaviourReferences.GetRequired<NetworkTransform>(
                playerObject,
                this
            );

        if (networkTransform != null)
            networkTransform.Teleport(spawnPosition, spawnRotation);

        playerObject.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
    }

    private bool PlayerAlreadyHasCharacter(PlayerRef player)
    {
        for (int i = 0; i < MaxCharacters; i++)
        {
            if (CharacterOwners[i] == player)
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

    private Color[] GetSelectableCharacterColors()
    {
        Color[] selectableColors = new Color[MaxCharacters];

        for (int i = 0; i < selectableColors.Length; i++)
            selectableColors[i] = GetConfiguredCharacterColor(i);

        return selectableColors;
    }

    private Color GetConfiguredCharacterColor(int characterIndex)
    {
        if (characterColors != null &&
            characterIndex >= 0 &&
            characterIndex < characterColors.Length)
        {
            return characterColors[characterIndex];
        }

        return characterIndex >= 0 &&
               characterIndex < DefaultCharacterColors.Length
            ? DefaultCharacterColors[characterIndex]
            : Color.white;
    }

    private int GetSpawnIndexForCharacter(int characterIndex)
    {
        if (CanSpawnOnHexMap())
            return Mathf.Clamp(
                characterIndex,
                0,
                MaxCharacters - 1
            );

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError(
                "Assign Hex Map Generator or at least one fallback Spawn Point on GameSceneManager."
            );
            return 0;
        }

        return Mathf.Abs(characterIndex) % spawnPoints.Length;
    }

    private Vector3 GetSpawnPositionByIndex(int spawnIndex)
    {
        if (TryGetHexMapSpawnPose(
                spawnIndex,
                out Vector3 hexSpawnPosition,
                out _))
        {
            return hexSpawnPosition;
        }

        if (spawnPoints == null)
        {
            Debug.LogError("SpawnPoints array is NULL on local GameSceneManager.");
            return Vector3.zero;
        }

        if (spawnPoints.Length == 0)
        {
            Debug.LogError("SpawnPoints array is EMPTY on local GameSceneManager.");
            return Vector3.zero;
        }

        spawnIndex = Mathf.Clamp(spawnIndex, 0, spawnPoints.Length - 1);

        if (spawnPoints[spawnIndex] == null)
        {
            Debug.LogError("Spawn point " + spawnIndex + " is NULL.");
            return Vector3.zero;
        }

        Debug.Log(
            "Using local spawn point " +
            spawnIndex +
            " at position " +
            spawnPoints[spawnIndex].position
        );

        return spawnPoints[spawnIndex].position;
    }

    private Quaternion GetSpawnRotationByIndex(int spawnIndex)
    {
        if (TryGetHexMapSpawnPose(
                spawnIndex,
                out _,
                out Quaternion hexSpawnRotation))
        {
            return hexSpawnRotation;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
            return Quaternion.identity;

        spawnIndex = Mathf.Clamp(spawnIndex, 0, spawnPoints.Length - 1);

        if (spawnPoints[spawnIndex] == null)
            return Quaternion.identity;

        return spawnPoints[spawnIndex].rotation;
    }

    private bool CanSpawnOnHexMap()
    {
        return
            spawnOnHexMapCorners &&
            hexMapGenerator != null &&
            hexMapGenerator.IsHexagonMap;
    }

    private bool TryGetHexMapSpawnPose(
        int spawnIndex,
        out Vector3 spawnPosition,
        out Quaternion spawnRotation)
    {
        spawnPosition = Vector3.zero;
        spawnRotation = Quaternion.identity;

        if (!CanSpawnOnHexMap())
            return false;

        Vector3 tileTopCenter;

        if (spawnIndex >= 0 &&
            spawnIndex < CornerSpawnCount)
        {
            if (!hexMapGenerator.TryGetHexagonCornerWorldPosition(
                    spawnIndex,
                    out tileTopCenter))
            {
                Debug.LogWarning(
                    $"Corner spawn {spawnIndex} is unavailable. Using a fallback Spawn Point.",
                    this
                );
                return false;
            }
        }
        else
        {
            int islandIndex =
                spawnIndex - CornerSpawnCount;

            if (islandSpawnCoordinates == null ||
                islandIndex < 0 ||
                islandIndex >= islandSpawnCoordinates.Length)
            {
                Debug.LogError(
                    $"Island spawn coordinate for color {spawnIndex + 1} is not assigned.",
                    this
                );
                return false;
            }

            HexCoord islandCoordinate =
                islandSpawnCoordinates[islandIndex];

            if (!hexMapGenerator.TryGetTile(
                    islandCoordinate,
                    out HexTile islandTile) ||
                islandTile == null)
            {
                Debug.LogError(
                    $"Island spawn coordinate {islandCoordinate} for color {spawnIndex + 1} does not contain a tile.",
                    this
                );
                return false;
            }

            tileTopCenter = islandTile.WorldTopCenter;
        }

        spawnPosition = tileTopCenter + Vector3.up * spawnHeightAboveTile;

        Vector3 directionToCenter =
            hexMapGenerator.MapCenterWorldPosition - spawnPosition;
        directionToCenter.y = 0f;

        if (directionToCenter.sqrMagnitude > 0.0001f)
            spawnRotation = Quaternion.LookRotation(directionToCenter.normalized);

        Debug.Log(
            "Using hex map spawn " +
            spawnIndex +
            " at position " +
            spawnPosition
        );

        return true;
    }

    private void OnOccupiedCharactersChanged()
    {
        characterSelectionUI?.Refresh(OccupiedCharactersMask);
    }

    private void OnMatchStartedChanged()
    {
        if (!MatchStarted)
            return;
       
        avatarSelectionManager?.HideSelectionUI();

        characterSelectionUI?.Hide();
        Debug.Log(
            "All connected players selected a color and avatar. Match started."
        );

        RefreshMatchTimerText();
    }

    private void TryStartMatchWhenAllPlayersSelected()
    {
        if (Object == null ||
            !Object.HasStateAuthority ||
            MatchStarted ||
            Runner == null)
        {
            return;
        }

        int activePlayerCount = 0;
        int selectedPlayerCount = 0;
        int spawnedPlayerCount = 0;

        foreach (PlayerRef activePlayer in Runner.ActivePlayers)
        {
            activePlayerCount++;

            if (PlayerAlreadyHasCharacter(activePlayer))
                selectedPlayerCount++;

            NetworkObject playerObject =
                Runner.GetPlayerObject(activePlayer);

            if (NetworkObjectBehaviourReferences.TryGet(
                    playerObject,
                    out HexBallPlayerController _))
            {
                spawnedPlayerCount++;
            }
        }

        if (activePlayerCount < MinimumPlayersToStart ||
            selectedPlayerCount != activePlayerCount ||
            spawnedPlayerCount != activePlayerCount)
        {
            return;
        }
        

        if (!avatarSelectionManager)
        {
            Debug.LogError("Avatar Selection Manager is missing.");
            return;
        }

        if (!avatarSelectionManager.AllActivePlayersHaveAvatar())
            return;
        
        MatchTimer = TickTimer.CreateFromSeconds(
            Runner,
            gameRulesSettings != null
                ? gameRulesSettings.MatchDurationSeconds
                : 150f
        );
        MatchStarted = true;
    }

    public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player)
    {
        ClaimDepartedPlayerObjects(player);

        if (!Object.HasStateAuthority)
            return;
        
        avatarSelectionManager?.ReleaseAvatar(player);

        if (hexTerritoryManager != null)
            hexTerritoryManager.ReleasePlayerTerritory(player);

        bool changed = false;

        for (int i = 0; i < MaxCharacters; i++)
        {
            if (CharacterOwners[i] != player)
                continue;

            CharacterOwners.Set(i, PlayerRef.None);
            OccupiedCharactersMask &= ~(1 << i);
            changed = true;
        }

        if (changed)
            Debug.Log("Released character of player: " + player.PlayerId);

        TryStartMatchWhenAllPlayersSelected();
    }

    private void ClaimDepartedPlayerObjects(PlayerRef departedPlayer)
    {
        if (Runner == null ||
            !Runner.IsSharedModeMasterClient)
        {
            return;
        }

        NetworkObject departedPlayerObject =
            Runner.GetPlayerObject(departedPlayer);

        foreach (NetworkObject networkObject in
                 Runner.GetAllNetworkObjects())
        {
            if (networkObject == null ||
                networkObject == departedPlayerObject ||
                networkObject.StateAuthority != departedPlayer)
            {
                continue;
            }

            NetworkObjectFlags flags = networkObject.Flags;
            bool allowsAuthorityOverride =
                (flags &
                 NetworkObjectFlags.AllowStateAuthorityOverride) != 0;
            bool followsMasterClient =
                (flags &
                 NetworkObjectFlags.MasterClientObject) != 0;
            bool isDestroyedWithAuthority =
                (flags &
                 NetworkObjectFlags.DestroyWhenStateAuthorityLeaves) != 0;

            if (!allowsAuthorityOverride ||
                followsMasterClient ||
                isDestroyedWithAuthority)
            {
                continue;
            }

            networkObject.RequestStateAuthority();
        }
    }

    public void StateAuthorityChanged()
    {
        RefreshEndGameButton();

        if (Object == null || !Object.HasStateAuthority)
            return;

        ReconcileCharacterOwners();

        Debug.Log(
            "GameSceneManager: this client is now responsible for the " +
            "authoritative match state."
        );
    }

    private void ReconcileCharacterOwners()
    {
        if (Runner == null ||
            Object == null ||
            !Object.HasStateAuthority)
        {
            return;
        }

        for (int characterIndex = 0;
             characterIndex < MaxCharacters;
             characterIndex++)
        {
            PlayerRef owner = CharacterOwners[characterIndex];

            if (owner != PlayerRef.None &&
                !IsActivePlayer(owner))
            {
                avatarSelectionManager?.ReleaseAvatar(owner);
                hexTerritoryManager?.ReleasePlayerTerritory(owner);

                CharacterOwners.Set(
                    characterIndex,
                    PlayerRef.None
                );
            }
        }

        foreach (PlayerRef activePlayer in Runner.ActivePlayers)
        {
            NetworkObject playerObject =
                Runner.GetPlayerObject(activePlayer);

            if (!NetworkObjectBehaviourReferences.TryGet(
                    playerObject,
                    out NetworkPlayerCharacter playerCharacter) ||
                !IsValidCharacterIndex(
                    playerCharacter.CharacterIndex))
            {
                continue;
            }

            CharacterOwners.Set(
                playerCharacter.CharacterIndex,
                activePlayer
            );
        }

        int rebuiltOccupiedMask = 0;

        for (int characterIndex = 0;
             characterIndex < MaxCharacters;
             characterIndex++)
        {
            if (CharacterOwners[characterIndex] != PlayerRef.None)
                rebuiltOccupiedMask |= 1 << characterIndex;
        }

        OccupiedCharactersMask = rebuiltOccupiedMask;
    }

    private bool IsActivePlayer(PlayerRef player)
    {
        if (Runner == null || player == PlayerRef.None)
            return false;

        foreach (PlayerRef activePlayer in Runner.ActivePlayers)
        {
            if (activePlayer == player)
                return true;
        }

        return false;
    }
    
    public void NotifyAvatarSelectionChanged()
    {
        if (!Object || !Object.HasStateAuthority)
            return;

        TryStartMatchWhenAllPlayersSelected();
    }

    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player) { }
    public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner callbackRunner) { }
    public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token) { }
    public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner callbackRunner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner callbackRunner,
        System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner,
        System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner callbackRunner) { }
    public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
    public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key,
        ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, float progress) { }
}
