using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EndGameButton : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private ScoreManager scoreManager;

    [Header("UI")]
    [SerializeField] private Button endGameButton;
    [SerializeField] private TMP_Text masterStatusText;

    private void Awake()
    {
        if (endGameButton != null)
        {
            endGameButton.onClick.RemoveListener(OnEndGameClicked);
            endGameButton.onClick.AddListener(OnEndGameClicked);
        }
        else
        {
            Debug.LogWarning("EndGameButton: button is not assigned.");
        }
    }

    public override void Spawned()
    {
        Debug.Log("EndGameButton spawned. Is MasterClient: " + Runner.IsSharedModeMasterClient);
        RefreshButton();
    }

    public override void Render()
    {
        RefreshButton();
    }

    private void OnDestroy()
    {
        if (endGameButton != null)
            endGameButton.onClick.RemoveListener(OnEndGameClicked);
    }

    private void OnEndGameClicked()
    {
        Debug.Log("End Game button clicked.");

        if (scoreManager == null)
        {
            Debug.LogWarning("EndGameButton: ScoreManager is not assigned.");
            return;
        }

        if (Runner == null)
        {
            Debug.LogWarning("EndGameButton: Runner is null.");
            return;
        }

        if (!Runner.IsSharedModeMasterClient)
        {
            Debug.LogWarning("Only MasterClient can end the game.");
            return;
        }

        if (scoreManager.IsEnded)
        {
            Debug.LogWarning("Game is already ended.");
            return;
        }

        scoreManager.RequestEndGame();
    }

    private void RefreshButton()
    {
        bool isMasterClient = Runner != null && Runner.IsSharedModeMasterClient;

        bool canEndGame =
            isMasterClient &&
            scoreManager != null &&
            !scoreManager.IsEnded;

        if (endGameButton != null)
        {
            endGameButton.gameObject.SetActive(isMasterClient);
            endGameButton.interactable = canEndGame;
        }

        if (masterStatusText != null)
        {
            masterStatusText.text = isMasterClient
                ? "MasterClient: can end game"
                : "";
        }
    }
}