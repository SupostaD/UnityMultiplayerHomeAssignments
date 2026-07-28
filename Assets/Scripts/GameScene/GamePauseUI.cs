using UnityEngine;
using UnityEngine.UI;

public class GamePauseUI : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button exitButton;

    private bool isOpen;

    private void Awake()
    {
        Hide();

        if (continueButton != null)
            continueButton.onClick.AddListener(Hide);

        if (exitButton != null)
            exitButton.onClick.AddListener(ExitGame);
    }

    private void Update()
    {
        if (GameSceneManager.Instance != null &&
            (GameSceneManager.Instance.IsGameEnded ||
             GameSceneManager.Instance.IsLocalPlayerEliminated))
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isOpen)
                Hide();
            else
                Show();
        }
    }

    private void OnDestroy()
    {
        if (continueButton != null)
            continueButton.onClick.RemoveListener(Hide);

        if (exitButton != null)
            exitButton.onClick.RemoveListener(ExitGame);
    }

    public void Show()
    {
        isOpen = true;

        if (pausePanel != null)
            pausePanel.SetActive(true);

        GameInputBlocker.BlockGameplayInput();
    }

    public void Hide()
    {
        isOpen = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        GameInputBlocker.UnblockGameplayInput();
    }

    public void CloseForGameEnd()
    {
        isOpen = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    private void ExitGame()
    {
        GameSceneManager.Instance?.LeaveCurrentGame();
    }
}
