using System;
using System.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class EndGameUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button exitToMainMenuButton;
    [SerializeField] private int mainMenuSceneBuildIndex = 0;

    private const int ShutdownTimeoutMilliseconds = 2000;

    private bool isExiting;
    private bool emergencyExitStarted;

    private void Awake()
    {
        Hide();

        if (exitToMainMenuButton != null)
            exitToMainMenuButton.onClick.AddListener(OnExitClicked);
    }

    private void OnDestroy()
    {
        if (exitToMainMenuButton != null)
            exitToMainMenuButton.onClick.RemoveListener(OnExitClicked);
    }

    private void Update()
    {
        if (isExiting || panelRoot == null || !panelRoot.activeInHierarchy)
            return;

        if (exitToMainMenuButton == null || !Input.GetMouseButtonDown(0))
            return;

        RectTransform buttonRect = exitToMainMenuButton.transform as RectTransform;

        if (buttonRect == null)
            return;

        if (RectTransformUtility.RectangleContainsScreenPoint(buttonRect, Input.mousePosition))
            OnExitClicked();
    }

    public void Show(string message)
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        if (messageText != null)
            messageText.text = message;

        if (exitToMainMenuButton != null)
            exitToMainMenuButton.interactable = true;
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void OnExitClicked()
    {
        if (isExiting)
            return;

        isExiting = true;

        if (exitToMainMenuButton != null)
            exitToMainMenuButton.interactable = false;

        if (GameSceneManager.Instance != null)
        {
            GameSceneManager.Instance.LeaveCurrentGame();
            EmergencyExitToMainMenuAfterDelay(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        EmergencyExitToMainMenu();
    }

    private async void EmergencyExitToMainMenuAfterDelay(int sourceSceneBuildIndex)
    {
        await Task.Delay(ShutdownTimeoutMilliseconds + 500);

        if (SceneManager.GetActiveScene().buildIndex != sourceSceneBuildIndex)
            return;

        EmergencyExitToMainMenu();
    }

    private async void EmergencyExitToMainMenu()
    {
        if (emergencyExitStarted)
            return;

        emergencyExitStarted = true;

        try
        {
            NetworkRunner currentRunner =
                NetworkRunnerPrefabReferences.Active != null
                    ? NetworkRunnerPrefabReferences.Active.Runner
                    : null;

            if (currentRunner == null)
            {
                Debug.LogError(
                    "EndGameUI: Active Network Runner reference is missing.",
                    this
                );
            }
            else
            {
                Task shutdownTask = currentRunner.Shutdown();
                await Task.WhenAny(shutdownTask, Task.Delay(ShutdownTimeoutMilliseconds));

                if (currentRunner != null)
                    Destroy(currentRunner.gameObject);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Emergency exit failed to cleanly shutdown runner: " + exception.Message);
        }
        finally
        {
            GameInputBlocker.UnblockGameplayInput();
            SceneManager.LoadScene(mainMenuSceneBuildIndex);
        }
    }
}
