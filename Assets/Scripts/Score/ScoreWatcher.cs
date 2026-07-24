using Fusion;
using UnityEngine;

public class ScoreWatcher : NetworkBehaviour
{
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private RaceGameManager raceGameManager;

    private bool requestedAutoEnd;

    public override void FixedUpdateNetwork()
    {
        if (Runner == null)
            return;

        if (!Runner.IsSharedModeMasterClient)
            return;

        if (scoreManager == null)
            return;
        
        if (!Object.HasStateAuthority)
            Object.RequestStateAuthority();

        if (raceGameManager != null && !raceGameManager.Object.HasStateAuthority)
            raceGameManager.Object.RequestStateAuthority();

        TryGiveFirstFinisherPoints();
        TryAutoEndGame();
    }

    private void TryGiveFirstFinisherPoints()
    {
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            NetworkObject playerObject = Runner.GetPlayerObject(player);

            if (playerObject == null)
                continue;

            RacePlayerController racePlayer =
                NetworkObjectBehaviourReferences.GetRequired<RacePlayerController>(
                    playerObject,
                    this
                );

            if (racePlayer == null)
                continue;

            if (!racePlayer.HasFinished)
                continue;
            
            scoreManager.TryGiveFirstFinishPoints(player);
            return;
        }
    }

    private void TryAutoEndGame()
    {
        if (requestedAutoEnd)
            return;

        if (raceGameManager == null)
            return;

        if (scoreManager.IsEnded)
        {
            requestedAutoEnd = true;
            return;
        }

        if (raceGameManager.Phase != RacePhase.Finished)
            return;

        requestedAutoEnd = true;
        scoreManager.RequestEndGame();
    }
}
