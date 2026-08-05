using System.Collections.Generic;
using System.Text;
using Fusion;

public enum HexMatchEndReason
{
    Timer = 0,
    Manual = 1,
    LastPlayerStanding = 2
}

public static class HexMatchResultsFormatter
{
    public static string Build(
        NetworkRunner runner,
        HexTerritoryManager territoryManager,
        HexMatchEndReason reason)
    {
        List<ResultEntry> results = new List<ResultEntry>();

        if (runner != null)
        {
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                results.Add(new ResultEntry
                {
                    Player = player,
                    PlayerName = GetShortPlayerName(player),
                    Strength = territoryManager != null
                        ? territoryManager.GetDisplayedStrength(player)
                        : 0,
                    IsDead = territoryManager != null &&
                             territoryManager.IsPlayerEliminated(player)
                });
            }
        }

        results.Sort(CompareResults);
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("MATCH RESULTS");
        builder.AppendLine();

        int aliveCount = CountAlivePlayers(results);
        AppendAlivePlayers(builder, results, aliveCount);
        AppendDeadPlayers(builder, results, aliveCount);
        builder.AppendLine();
        AppendWinnerSummary(builder, results, aliveCount);
        builder.AppendLine(GetEndReasonText(reason));
        return builder.ToString();
    }

    private static int CountAlivePlayers(List<ResultEntry> results)
    {
        int aliveCount = 0;

        while (aliveCount < results.Count && !results[aliveCount].IsDead)
            aliveCount++;

        return aliveCount;
    }

    private static void AppendAlivePlayers(
        StringBuilder builder,
        List<ResultEntry> results,
        int aliveCount)
    {
        int resultIndex = 0;

        while (resultIndex < aliveCount)
        {
            int groupEnd = resultIndex + 1;
            int groupStrength = results[resultIndex].Strength;

            while (groupEnd < aliveCount &&
                   results[groupEnd].Strength == groupStrength)
            {
                groupEnd++;
            }

            bool isDraw = groupEnd - resultIndex > 1;
            int place = resultIndex + 1;

            for (int index = resultIndex; index < groupEnd; index++)
            {
                ResultEntry result = results[index];
                builder.Append(place);
                builder.Append(". ");
                builder.Append(result.PlayerName);
                builder.Append(" - ");
                builder.Append(result.Strength);
                builder.Append(" STR");

                if (isDraw)
                    builder.Append(" - DRAW");

                builder.AppendLine();
            }

            resultIndex = groupEnd;
        }
    }

    private static void AppendDeadPlayers(
        StringBuilder builder,
        List<ResultEntry> results,
        int aliveCount)
    {
        for (int index = aliveCount; index < results.Count; index++)
        {
            builder.Append("Last. ");
            builder.Append(results[index].PlayerName);
            builder.AppendLine(" - DEAD");
        }
    }

    private static int CompareResults(ResultEntry first, ResultEntry second)
    {
        if (first.IsDead != second.IsDead)
            return first.IsDead ? 1 : -1;

        int strengthComparison =
            second.Strength.CompareTo(first.Strength);

        return strengthComparison != 0
            ? strengthComparison
            : first.Player.PlayerId.CompareTo(second.Player.PlayerId);
    }

    private static void AppendWinnerSummary(
        StringBuilder builder,
        List<ResultEntry> results,
        int aliveCount)
    {
        if (aliveCount <= 0)
        {
            builder.AppendLine("Winner: nobody - all players are dead");
            return;
        }

        int bestStrength = results[0].Strength;
        int winnerCount = 1;

        while (winnerCount < aliveCount &&
               results[winnerCount].Strength == bestStrength)
        {
            winnerCount++;
        }

        if (winnerCount == 1)
        {
            builder.Append("Winner: ");
            builder.AppendLine(results[0].PlayerName);
            return;
        }

        builder.AppendLine("Winners: DRAW at first place");
    }

    private static string GetShortPlayerName(PlayerRef player)
    {
        string playerName = GameChatNetwork.Instance != null
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

    private static string GetEndReasonText(HexMatchEndReason reason)
    {
        switch (reason)
        {
            case HexMatchEndReason.Manual:
                return "Ended by MasterClient";
            case HexMatchEndReason.LastPlayerStanding:
                return "Last player standing";
            default:
                return "Time is over";
        }
    }

    private struct ResultEntry
    {
        public PlayerRef Player;
        public string PlayerName;
        public int Strength;
        public bool IsDead;
    }
}
