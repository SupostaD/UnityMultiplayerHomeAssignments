using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;

public class ScoreManager : NetworkBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Scores")]
    [SerializeField] private int pointsPerHit = 1;
    [SerializeField] private int pointsForFirstFinish = 10;

    [Networked, Capacity(16)]
    private NetworkDictionary<PlayerRef, int> Scores => default;

    [Networked, Capacity(16)]
    private NetworkDictionary<PlayerRef, NetworkString<_32>> PlayerNames => default;

    [Networked]
    public NetworkBool IsEnded { get; private set; }

    [Networked]
    private PlayerRef EndedBy { get; set; }

    [Networked]
    private NetworkBool FirstFinishAwarded { get; set; }

    [Networked]
    private PlayerRef FirstFinisher { get; set; }

    [Networked, OnChangedRender(nameof(OnEndGameChanged))]
    private int EndGameVersion { get; set; }

    private bool resultsShown;
    private bool pendingEndGameRequest;

    private void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        RegisterLocalPlayerName();

        if (Object.HasStateAuthority)
            EnsureActivePlayersHaveScores();

        if (IsEnded)
            ShowResultsPanel();
    }

    public override void FixedUpdateNetwork()
    {
        if (Runner != null && Runner.IsSharedModeMasterClient && !Object.HasStateAuthority)
            Object.RequestStateAuthority();

        if (Object.HasStateAuthority)
            EnsureActivePlayersHaveScores();

        if (pendingEndGameRequest && Object.HasStateAuthority)
        {
            pendingEndGameRequest = false;
            EndGameInternal(Runner.LocalPlayer);
        }
    }

    public override void Render()
    {
        if (IsEnded)
            ShowResultsPanel();
    }

    private void EnsureActivePlayersHaveScores()
    {
        if (Runner == null)
            return;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Scores.ContainsKey(player))
                Scores.Set(player, 0);

            if (!PlayerNames.ContainsKey(player))
                PlayerNames.Set(player, "Player " + player.PlayerId);
        }
    }

    private void RegisterLocalPlayerName()
    {
        if (Runner == null)
            return;

        string playerName = ChatLocalData.PlayerName;

        if (string.IsNullOrWhiteSpace(playerName))
            playerName = "Player " + Runner.LocalPlayer.PlayerId;

        playerName = playerName.Trim();

        if (playerName.Length > 24)
            playerName = playerName.Substring(0, 24);

        if (Object.HasStateAuthority)
        {
            RegisterPlayerNameInternal(Runner.LocalPlayer, playerName);
            return;
        }

        RPC_RegisterPlayerName(Runner.LocalPlayer, playerName);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RegisterPlayerName(PlayerRef player, NetworkString<_32> playerName)
    {
        RegisterPlayerNameInternal(player, playerName.ToString());
    }

    private void RegisterPlayerNameInternal(PlayerRef player, string playerName)
    {
        if (player == PlayerRef.None)
            return;

        if (string.IsNullOrWhiteSpace(playerName))
            playerName = "Player " + player.PlayerId;

        PlayerNames.Set(player, playerName);
    }

    public void AddHitPoint(PlayerRef player)
    {
        AddScore(player, pointsPerHit);
    }

    public void TryGiveFirstFinishPoints(PlayerRef player)
    {
        if (player == PlayerRef.None)
            return;

        if (Object.HasStateAuthority)
        {
            TryGiveFirstFinishPointsInternal(player);
            return;
        }

        RPC_TryGiveFirstFinishPoints(player);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_TryGiveFirstFinishPoints(PlayerRef player)
    {
        TryGiveFirstFinishPointsInternal(player);
    }

    private void TryGiveFirstFinishPointsInternal(PlayerRef player)
    {
        if (IsEnded)
            return;
        
        if (FirstFinishAwarded)
            return;

        FirstFinishAwarded = true;
        FirstFinisher = player;

        AddScoreInternal(player, pointsForFirstFinish);

        Debug.Log("First finisher bonus: Player " + player.PlayerId + " +" + pointsForFirstFinish);
    }

    private void AddScore(PlayerRef player, int amount)
    {
        if (player == PlayerRef.None)
            return;

        if (amount <= 0)
            return;

        if (Object.HasStateAuthority)
        {
            AddScoreInternal(player, amount);
            return;
        }

        RPC_AddScore(player, amount);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_AddScore(PlayerRef player, int amount)
    {
        AddScoreInternal(player, amount);
    }

    private void AddScoreInternal(PlayerRef player, int amount)
    {
        if (IsEnded)
            return;

        int currentScore = 0;

        if (Scores.ContainsKey(player))
            currentScore = Scores.Get(player);

        Scores.Set(player, currentScore + amount);

        Debug.Log("Score added: " + GetPlayerName(player) + " +" + amount);
    }

    public int GetScore(PlayerRef player)
    {
        if (Scores.ContainsKey(player))
            return Scores.Get(player);

        return 0;
    }

    public string GetPlayerName(PlayerRef player)
    {
        if (PlayerNames.ContainsKey(player))
        {
            string playerName = PlayerNames.Get(player).ToString();

            if (!string.IsNullOrWhiteSpace(playerName))
                return playerName;
        }

        return "Player " + player.PlayerId;
    }

    public void RequestEndGame()
    {
        if (Runner == null)
            return;

        if (!Runner.IsSharedModeMasterClient)
        {
            Debug.LogWarning("Only MasterClient can end the game.");
            return;
        }

        if (Object.HasStateAuthority)
        {
            EndGameInternal(Runner.LocalPlayer);
            return;
        }
        
        pendingEndGameRequest = true;
        Object.RequestStateAuthority();

        Debug.Log("MasterClient requested ScoreManager state authority.");
    }

    private void EndGameInternal(PlayerRef endedBy)
    {
        if (IsEnded)
            return;

        EnsureActivePlayersHaveScores();

        IsEnded = true;
        EndedBy = endedBy;
        EndGameVersion++;

        Debug.Log("Race ended by MasterClient: " + GetPlayerName(endedBy));
    }

    private void OnEndGameChanged()
    {
        ShowResultsPanel();
    }

    private void ShowResultsPanel()
    {
        if (resultsShown)
            return;

        EndGamePanel panel = EndGamePanel.Instance;

        if (panel == null)
            return;

        resultsShown = true;
        panel.Show(BuildResultsText());

        GameInputBlocker.BlockGameplayInput();
    }

    public string BuildResultsText()
    {
        List<ResultEntry> results = new List<ResultEntry>();

        foreach (KeyValuePair<PlayerRef, int> pair in Scores)
        {
            results.Add(new ResultEntry
            {
                Player = pair.Key,
                Score = pair.Value
            });
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        StringBuilder builder = new StringBuilder();

        builder.AppendLine("RESULTS");
        builder.AppendLine("");

        for (int i = 0; i < results.Count; i++)
        {
            ResultEntry result = results[i];

            builder.Append(i + 1);
            builder.Append(". ");
            builder.Append(GetPlayerName(result.Player));
            builder.Append(" - ");
            builder.Append(result.Score);
            builder.AppendLine(" points");
        }

        builder.AppendLine("");

        if (FirstFinishAwarded)
        {
            builder.Append("Winner bonus: ");
            builder.Append(GetPlayerName(FirstFinisher));
            builder.AppendLine(" +10");
        }
        else
        {
            builder.AppendLine("Winner bonus: nobody");
        }

        builder.Append("Ended by MasterClient: ");
        builder.Append(GetPlayerName(EndedBy));

        return builder.ToString();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private struct ResultEntry
    {
        public PlayerRef Player;
        public int Score;
    }
}