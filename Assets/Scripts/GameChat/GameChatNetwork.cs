using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class GameChatNetwork : NetworkBehaviour
{
    public static GameChatNetwork Instance { get; private set; }

    private NetworkRunner cachedRunner;

    private readonly Dictionary<PlayerRef, string> playerNames = new Dictionary<PlayerRef, string>();

    private const int MaxMessageCharacters = 60;
    private const int MaxNameCharacters = 24;

    private void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        cachedRunner = Runner;

        RegisterLocalName();

        InvokeRepeating(nameof(RegisterLocalName), 1f, 2f);
    }

    private void RegisterLocalName()
    {
        NetworkRunner runner = GetRunner();

        if (runner == null)
            return;

        string name = PrepareName(ChatLocalData.PlayerName);

        RPC_RegisterName(runner.LocalPlayer, name);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_RegisterName(PlayerRef player, NetworkString<_32> playerName)
    {
        string name = playerName.ToString();

        if (string.IsNullOrWhiteSpace(name))
            name = "Player " + player.PlayerId;

        playerNames[player] = name;
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
        string finalName = senderName.ToString();

        if (string.IsNullOrWhiteSpace(finalName))
            finalName = GetPlayerName(senderPlayer);

        GameChatUI.Instance?.AddGlobalMessage(finalName, message.ToString());
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

        GameChatUI.Instance?.AddPrivateMessage(finalName, message.ToString());
    }

    public string GetPlayerName(PlayerRef player)
    {
        if (playerNames.TryGetValue(player, out string name))
            return name;

        return "Player " + player.PlayerId;
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
        if (cachedRunner != null)
            return cachedRunner;

        cachedRunner = FindObjectOfType<NetworkRunner>();
        return cachedRunner;
    }

    private void OnDestroy()
    {
        CancelInvoke();

        if (Instance == this)
            Instance = null;
    }
}