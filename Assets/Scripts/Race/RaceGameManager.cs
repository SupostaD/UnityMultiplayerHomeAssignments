using Fusion;
using TMPro;
using UnityEngine;

public class RaceGameManager : NetworkBehaviour
{
    [Header("Race Rules")]
    [SerializeField] private int totalLaps = 2;
    [SerializeField] private float countdownSeconds = 5f;
    [SerializeField] private float startMessageSeconds = 1f;

    [Header("Track")]
    [SerializeField] private RaceCheckpoint[] checkpoints;

    [Header("UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField] private TMP_Text lapText;

    [Header("UI Options")]
    [SerializeField] private bool hideCountdownTextWhenInactive = true;

    [Networked] public RacePhase Phase { get; private set; }
    [Networked] public float CountdownRemaining { get; private set; }
    [Networked] private float StartMessageRemaining { get; set; }
    [Networked] private int ReadyPlayers { get; set; }
    [Networked] private int ActivePlayers { get; set; }

    public static RaceGameManager Instance { get; private set; }

    public int TotalLaps => Mathf.Max(1, totalLaps);
    public int CheckpointCount { get; private set; }
    public bool CanPlayersMove => Phase == RacePhase.Starting || Phase == RacePhase.Racing;
    public bool IsRaceInProgress => Phase == RacePhase.Starting || Phase == RacePhase.Racing;

    private void Awake()
    {
        Instance = this;
        CacheCheckpoints();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void Spawned()
    {
        CacheCheckpoints();

        if (Object.HasStateAuthority)
        {
            Phase = RacePhase.WaitingForPlayers;
            CountdownRemaining = 0f;
            StartMessageRemaining = 0f;
            UpdateReadyPlayers();
        }

        RefreshUI();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        UpdateReadyPlayers();

        switch (Phase)
        {
            case RacePhase.WaitingForPlayers:
                if (ActivePlayers > 0 && ReadyPlayers == ActivePlayers)
                    StartCountdown();
                break;

            case RacePhase.Countdown:
                if (ActivePlayers == 0 || ReadyPlayers < ActivePlayers)
                {
                    Phase = RacePhase.WaitingForPlayers;
                    CountdownRemaining = 0f;
                    break;
                }

                CountdownRemaining -= Runner.DeltaTime;

                if (CountdownRemaining <= 0f)
                {
                    CountdownRemaining = 0f;
                    Phase = RacePhase.Starting;
                    StartMessageRemaining = Mathf.Max(0.1f, startMessageSeconds);
                }
                break;

            case RacePhase.Starting:
                StartMessageRemaining -= Runner.DeltaTime;

                if (StartMessageRemaining <= 0f)
                {
                    StartMessageRemaining = 0f;
                    Phase = RacePhase.Racing;
                }
                break;

            case RacePhase.Racing:
                if (AllRacePlayersFinished())
                    Phase = RacePhase.Finished;
                break;
        }
    }

    public override void Render()
    {
        RefreshUI();
    }

    private void CacheCheckpoints()
    {
        if (checkpoints == null || checkpoints.Length == 0)
        {
            Debug.LogError(
                "RaceGameManager: Checkpoints are not assigned.",
                this
            );
            CheckpointCount = 1;
            return;
        }

        int highestCheckpointIndex = -1;

        if (checkpoints != null)
        {
            foreach (RaceCheckpoint checkpoint in checkpoints)
            {
                if (checkpoint == null)
                    continue;

                highestCheckpointIndex = Mathf.Max(highestCheckpointIndex, checkpoint.CheckpointIndex);
            }
        }

        CheckpointCount = Mathf.Max(1, highestCheckpointIndex + 1);
    }

    private void StartCountdown()
    {
        Phase = RacePhase.Countdown;
        CountdownRemaining = Mathf.Max(1f, countdownSeconds);
        StartMessageRemaining = 0f;
    }

    private void UpdateReadyPlayers()
    {
        int activePlayers = 0;
        int readyPlayers = 0;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            activePlayers++;

            NetworkObject playerObject = Runner.GetPlayerObject(player);

            if (playerObject == null)
                continue;

            if (NetworkObjectBehaviourReferences.TryGet(
                    playerObject,
                    out RacePlayerController _))
                readyPlayers++;
        }

        ActivePlayers = activePlayers;
        ReadyPlayers = readyPlayers;
    }

    private bool AllRacePlayersFinished()
    {
        int racePlayers = 0;
        int finishedPlayers = 0;

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

            racePlayers++;

            if (racePlayer.HasFinished)
                finishedPlayers++;
        }

        return racePlayers > 0 && finishedPlayers == racePlayers;
    }

    private void RefreshUI()
    {
        RefreshStatusText();
        RefreshCountdownText();
        RefreshLapText();
    }

    private void RefreshStatusText()
    {
        if (statusText == null)
            return;

        switch (Phase)
        {
            case RacePhase.WaitingForPlayers:
                statusText.text = "Waiting for players: " + ReadyPlayers + "/" + ActivePlayers;
                break;

            case RacePhase.Countdown:
                statusText.text = "Get ready";
                break;

            case RacePhase.Starting:
            case RacePhase.Racing:
                statusText.text = "Race started";
                break;

            case RacePhase.Finished:
                statusText.text = "Race finished";
                break;
        }
    }

    private void RefreshCountdownText()
    {
        if (countdownText == null)
            return;

        if (Phase == RacePhase.Countdown)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = Mathf.CeilToInt(CountdownRemaining).ToString();
            return;
        }

        if (Phase == RacePhase.Starting)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = "START";
            return;
        }

        if (hideCountdownTextWhenInactive)
            countdownText.gameObject.SetActive(false);
        else
            countdownText.text = string.Empty;
    }

    private void RefreshLapText()
    {
        if (lapText == null || Runner == null)
            return;

        NetworkObject localPlayerObject = Runner.GetPlayerObject(Runner.LocalPlayer);
        RacePlayerController localRacePlayer = localPlayerObject == null
            ? null
            : NetworkObjectBehaviourReferences.GetRequired<RacePlayerController>(
                localPlayerObject,
                this
            );

        if (localRacePlayer == null)
        {
            lapText.text = "Lap: -/" + TotalLaps;
            return;
        }

        if (localRacePlayer.HasFinished)
        {
            lapText.text = "Finished";
            return;
        }

        int visibleLap = Mathf.Clamp(localRacePlayer.CurrentLap + 1, 1, TotalLaps);
        lapText.text = "Lap: " + visibleLap + "/" + TotalLaps;
    }
}
