using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EndGamePanel : MonoBehaviour
{
    public static EndGamePanel Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text resultsText;
    [SerializeField] private Button closeButton;

    private void Awake()
    {
        Instance = this;

        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Hide);

        if (Instance == this)
            Instance = null;
    }

    public void Show(string results)
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        if (resultsText != null)
            resultsText.text = results;

        GameInputBlocker.BlockGameplayInput();
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        GameInputBlocker.UnblockGameplayInput();
    }
}