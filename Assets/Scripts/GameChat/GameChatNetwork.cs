using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class GameChatNetwork : NetworkBehaviour
{
    [System.Serializable]
    private sealed class PlayerProfileJson
    {
        public int playerId;
        public string playerName;
        public int characterIndex;
    }

    public static GameChatNetwork Instance { get; private set; }

    private NetworkRunner cachedRunner;

    private readonly Dictionary<PlayerRef, string> playerNames = new Dictionary<PlayerRef, string>();
    private readonly Dictionary<PlayerRef, int> playerCharacterIndices =
        new Dictionary<PlayerRef, int>();

    private const int MaxMessageCharacters = 60;
    private const int MaxNameCharacters = 24;

    private void Awake()
    {
        Instance = this;
    }

    private void RegisterLocalName()
    {
        NetworkRunner runner = GetRunner();

        if (runner == null)
            return;

        string name = PrepareName(ChatLocalData.PlayerName);
        PlayerRef localPlayer = runner.LocalPlayer;
        PlayerProfileJson profile = new PlayerProfileJson
        {
            playerId = localPlayer.PlayerId,
            playerName = name,
            characterIndex = GetPlayerCharacterIndex(localPlayer)
        };
        string serializedProfile = JsonUtility.ToJson(profile);

        RPC_RegisterPlayerProfileJson(localPlayer, serializedProfile);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_RegisterPlayerProfileJson(
        PlayerRef player,
        NetworkString<_128> serializedProfile)
    {
        PlayerProfileJson profile;

        try
        {
            profile = JsonUtility.FromJson<PlayerProfileJson>(
                serializedProfile.ToString()
            );
        }
        catch (System.ArgumentException exception)
        {
            Debug.LogWarning(
                "GameChatNetwork: Invalid player profile JSON. " +
                exception.Message
            );
            return;
        }

        if (profile == null || profile.playerId != player.PlayerId)
        {
            Debug.LogWarning(
                "GameChatNetwork: Player profile JSON validation failed."
            );
            return;
        }

        string name = PrepareName(profile.playerName);

        if (string.IsNullOrWhiteSpace(name))
            name = "Player " + player.PlayerId;

        playerNames[player] = name;
        playerCharacterIndices[player] = profile.characterIndex;
    }

    public void SendGlobalMessage(string message)
    {
        message = PrepareMessage(message);

        if (string.IsNullOrWhiteSpace(message))
            return;

        NetworkRunner runner = GetRunner();

        if (runner == null)
        {
            Debug.LogWarning("GameChatNetwork: Runner not found.");
            return;
        }

        string senderName = PrepareName(ChatLocalData.PlayerName);

        RPC_SendGlobalMessage(runner.LocalPlayer, senderName, message);
    }

    public void SendPrivateMessage(PlayerRef targetPlayer, string message)
    {
        message = PrepareMessage(message);

        if (string.IsNullOrWhiteSpace(message))
            return;

        NetworkRunner runner = GetRunner();

        if (runner == null)
        {
            Debug.LogWarning("GameChatNetwork: Runner not found.");
            return;
        }

        string senderName = PrepareName(ChatLocalData.PlayerName);

        RPC_SendPrivateMessage(runner.LocalPlayer, targetPlayer, senderName, message);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_SendGlobalMessage(
        PlayerRef senderPlayer,
        NetworkString<_32> senderName,
        NetworkString<_64> message)
    {
        NetworkRunner runner = GetRunner();

        if (runner == null)
            return;

        if (runner.LocalPlayer == senderPlayer)
            return;

        string finalName = senderName.ToString();

        if (string.IsNullOrWhiteSpace(finalName))
            finalName = GetPlayerName(senderPlayer);

        int characterIndex = GetPlayerCharacterIndex(senderPlayer);

        GameChatUI.Instance?.AddGlobalMessage(finalName, characterIndex, message.ToString());
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_SendPrivateMessage(
        PlayerRef senderPlayer,
        PlayerRef targetPlayer,
        NetworkString<_32> senderName,
        NetworkString<_64> message)
    {
        NetworkRunner runner = GetRunner();

        if (runner == null)
            return;

        if (runner.LocalPlayer != targetPlayer)
            return;

        string finalName = senderName.ToString();

        if (string.IsNullOrWhiteSpace(finalName))
            finalName = GetPlayerName(senderPlayer);

        int characterIndex = GetPlayerCharacterIndex(senderPlayer);

        GameChatUI.Instance?.AddPrivateMessage(finalName, characterIndex, message.ToString());
    }

    public string GetPlayerName(PlayerRef player)
    {
        if (playerNames.TryGetValue(player, out string name))
            return name;

        return "Player " + player.PlayerId;
    }

    public int GetPlayerCharacterIndex(PlayerRef player)
    {
        NetworkRunner runner = GetRunner();

        if (runner == null)
            return GetCachedPlayerCharacterIndex(player);

        NetworkObject playerObject = runner.GetPlayerObject(player);

        if (playerObject == null)
            return GetCachedPlayerCharacterIndex(player);

        if (!NetworkObjectBehaviourReferences.TryGet(
                playerObject,
                out NetworkPlayerCharacter playerCharacter))
        {
            return GetCachedPlayerCharacterIndex(player);
        }

        return playerCharacter.CharacterIndex;
    }

    private int GetCachedPlayerCharacterIndex(PlayerRef player)
    {
        return playerCharacterIndices.TryGetValue(player, out int characterIndex)
            ? characterIndex
            : 0;
    }

    public Color GetPlayerColor(PlayerRef player)
    {
        int characterIndex = GetPlayerCharacterIndex(player);

        if (GameSceneManager.Instance == null)
            return Color.white;

        return GameSceneManager.Instance.GetCharacterColor(characterIndex);
    }

    public string GetCharacterColorHex(int characterIndex)
    {
        Color color = Color.white;

        if (GameSceneManager.Instance != null)
            color = GameSceneManager.Instance.GetCharacterColor(characterIndex);

        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    private string PrepareMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        message = message.Trim();

        if (message.Length > MaxMessageCharacters)
            message = message.Substring(0, MaxMessageCharacters);

        return message;
    }

    private string PrepareName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "Player";

        name = name.Trim();

        if (name.Length > MaxNameCharacters)
            name = name.Substring(0, MaxNameCharacters);

        return name;
    }

    private NetworkRunner GetRunner()
    {
        return cachedRunner;
    }
    public override void Spawned()
    {
        cachedRunner = Runner;

        RegisterLocalName();
        InvokeRepeating(nameof(RegisterLocalName), 1f, 2f);
    }
    private void OnDestroy()
    {
        CancelInvoke();

        if (Instance == this)
            Instance = null;
    }
    
    public bool IsReady()
    {
        return cachedRunner != null;
    }

    public IEnumerable<PlayerRef> GetActivePlayers()
    {
        if (cachedRunner == null)
            yield break;

        foreach (PlayerRef player in cachedRunner.ActivePlayers)
            yield return player;
    }

    public PlayerRef GetLocalPlayer()
    {
        if (cachedRunner == null)
            return PlayerRef.None;

        return cachedRunner.LocalPlayer;
    }
    
    public int GetLocalPlayerCharacterIndex()
    {
        NetworkRunner runner = GetRunner();

        if (runner == null)
            return 0;

        return GetPlayerCharacterIndex(runner.LocalPlayer);
    }
}
