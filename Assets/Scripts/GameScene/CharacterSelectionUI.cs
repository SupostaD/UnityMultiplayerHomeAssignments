using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectionUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button[] characterButtons = new Button[10];
    [SerializeField] private Image[] characterButtonImages = new Image[10];
    [SerializeField] private TMP_Text[] characterButtonTexts = new TMP_Text[10];

    [Header("Labels")]
    [SerializeField] private string[] optionNames =
    {
        "Red",
        "Blue",
        "Green",
        "Yellow",
        "Cyan",
        "Magenta",
        "Orange",
        "Purple",
        "Black",
        "White"
    };

    [Header("Texts")]
    [SerializeField] private TMP_Text statusText;

    private Color[] characterColors;
    private bool waitingForMatchStart;
    private int lastOccupiedCharactersMask;

    public void Init(Color[] colors)
    {
        characterColors = colors;
        waitingForMatchStart = false;

        for (int i = 0; i < characterButtons.Length; i++)
        {
            int characterIndex = i;

            if (!characterButtons[i]) continue;

            bool isSelectable = characterColors != null && i < characterColors.Length;
            characterButtons[i].gameObject.SetActive(isSelectable);

            if (!isSelectable)
                continue;

            characterButtons[i].onClick.RemoveAllListeners();
            characterButtons[i].onClick.AddListener(() =>
            {
                GameSceneManager.Instance.RequestCharacter(characterIndex);
            });

            ApplyButtonColor(characterButtons[i], characterIndex);
        }
    }

    public void Refresh(int occupiedCharactersMask)
    {
        lastOccupiedCharactersMask = occupiedCharactersMask;

        for (int i = 0; i < characterButtons.Length; i++)
        {
            if (!characterButtons[i]) continue;

            bool isSelectable = characterColors != null && i < characterColors.Length;
            characterButtons[i].gameObject.SetActive(isSelectable);

            if (!isSelectable)
                continue;

            bool isOccupied = (occupiedCharactersMask & (1 << i)) != 0;

            characterButtons[i].interactable =
                !isOccupied &&
                !waitingForMatchStart;

            TMP_Text buttonText =
                characterButtonTexts != null && i < characterButtonTexts.Length
                    ? characterButtonTexts[i]
                    : null;

            if (buttonText)
            {
                string optionName = optionNames != null && i < optionNames.Length
                    ? optionNames[i]
                    : $"Color {i + 1}";

                buttonText.text = isOccupied
                    ? $"{optionName}\nOccupied"
                    : $"{optionName}\nFree";
            }
        }
    }

    public void SetStatus(string message)
    {
        Debug.Log(message);

        if (statusText) statusText.text = message;
    }

    public void ShowWaitingForMatchStart()
    {
        waitingForMatchStart = true;
        Refresh(lastOccupiedCharactersMask);
        SetStatus("Waiting for all players to choose a color");
        Show();
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void ApplyButtonColor(Button button, int characterIndex)
    {
        Image image =
            characterButtonImages != null &&
            characterIndex >= 0 &&
            characterIndex < characterButtonImages.Length
                ? characterButtonImages[characterIndex]
                : null;

        if (image == null)
            image = button.targetGraphic as Image;

        if (!image)
        {
            Debug.LogError(
                $"CharacterSelectionUI: Button Image {characterIndex} is not assigned.",
                this
            );
            return;
        }

        if (characterColors == null) return;

        if (characterIndex < 0 || characterIndex >= characterColors.Length) 
            return;

        image.color = characterColors[characterIndex];
    }
}
