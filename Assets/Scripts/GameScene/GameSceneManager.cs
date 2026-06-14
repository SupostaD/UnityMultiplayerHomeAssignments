using System;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class GameSceneManager : NetworkBehaviour, INetworkRunnerCallbacks
{
    [Header("Player")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    [Header("Character Selection")]
    [SerializeField] private CharacterSelectionUI characterSelectionUI;
    [SerializeField] private Color[] characterColors = new Color[10];

    [Networked, OnChangedRender(nameof(OnOccupiedCharactersChanged))]
    private int OccupiedCharactersMask { get; set; }

    public static GameSceneManager Instance { get; private set; }

    private const int MaxCharacters = 10;

    private readonly PlayerRef[] characterOwners = new PlayerRef[MaxCharacters];

    private NetworkRunner runner;
    private NetworkObject localPlayerObject;

    private void Awake()
    {
        Instance = this;

        for (int i = 0; i < characterOwners.Length; i++)
        {
            characterOwners[i] = PlayerRef.None;
        }
    }

    public override void Spawned()
    {
        runner = Runner;
        runner.AddCallbacks(this);

        if (Object.HasStateAuthority) OccupiedCharactersMask = 0;

        if (characterSelectionUI)
        {
            characterSelectionUI.Init(characterColors);
            characterSelectionUI.Refresh(OccupiedCharactersMask);
            characterSelectionUI.Show();
            characterSelectionUI.SetStatus("Choose a character");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (runner) runner.RemoveCallbacks(this);
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

        if (localPlayerObject)
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
        if (!runner) runner = Runner;

        if (runner.LocalPlayer != targetPlayer) return;

        SpawnLocalPlayer(characterIndex, spawnPosition, spawnYRotation);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_CharacterRejected(
        [RpcTarget] PlayerRef targetPlayer,
        string reason)
    {
        if (!runner) runner = Runner;

        if (runner.LocalPlayer != targetPlayer)
            return;

        characterSelectionUI?.SetStatus(reason);
        characterSelectionUI?.Show();
    }

    private void SpawnLocalPlayer(int characterIndex, Vector3 spawnPosition, float spawnYRotation)
    {
        if (!runner) runner = Runner;

        if (!playerPrefab)
        {
            Debug.LogError("Player Prefab is not assigned.");
            return;
        }

        if (localPlayerObject) return;

        Quaternion spawnRotation = Quaternion.Euler(0f, spawnYRotation, 0f);

        localPlayerObject = runner.Spawn(
            playerPrefab,
            spawnPosition,
            spawnRotation,
            runner.LocalPlayer
        );

       NetworkPlayerCharacter playerCharacter = localPlayerObject.GetComponent<NetworkPlayerCharacter>();

        if (playerCharacter) playerCharacter.CharacterIndex = characterIndex;

        runner.SetPlayerObject(runner.LocalPlayer, localPlayerObject);

        characterSelectionUI?.Hide();
    }

    private bool PlayerAlreadyHasCharacter(PlayerRef player)
    {
        for (int i = 0; i < characterOwners.Length; i++)
        {
            if (characterOwners[i] == player) return true;
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
        if (!Object.HasStateAuthority) return;

        bool changed = false;

        for (int i = 0; i < characterOwners.Length; i++)
        {
            if (characterOwners[i] != player) continue;

            characterOwners[i] = PlayerRef.None;
            OccupiedCharactersMask &= ~(1 << i);
            changed = true;
        }

        if (changed) Debug.Log("Released character of player: " + player.PlayerId);
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