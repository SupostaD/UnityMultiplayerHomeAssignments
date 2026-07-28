using Fusion;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkRttDisplay : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.25f;

    [Header("Required References")]
    [SerializeField] private GameSceneManager gameSceneManager;
    [SerializeField] private TMP_Text rttText;

    private float nextRefreshTime;

    private void Awake()
    {
        if (gameSceneManager == null)
        {
            Debug.LogError(
                "NetworkRttDisplay: Game Scene Manager is not assigned.",
                this
            );
        }

        if (rttText == null)
        {
            Debug.LogError(
                "NetworkRttDisplay: RTT Text is not assigned.",
                this
            );
        }
    }

    private void Update()
    {
        if (rttText == null ||
            Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime =
            Time.unscaledTime +
            RefreshIntervalSeconds;

        NetworkRunner runner =
            gameSceneManager != null
                ? gameSceneManager.ActiveRunner
                : null;

        if (runner == null ||
            !runner.IsRunning ||
            runner.LocalPlayer == PlayerRef.None)
        {
            rttText.text = "RTT: -- ms";
            return;
        }

        double rttSeconds =
            runner.GetPlayerRtt(PlayerRef.None);
        int rttMilliseconds =
            Mathf.Max(
                0,
                Mathf.RoundToInt((float)(rttSeconds * 1000.0))
            );

        rttText.text = $"RTT: {rttMilliseconds} ms";
    }
}
