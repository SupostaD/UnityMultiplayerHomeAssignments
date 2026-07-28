using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectionUI : MonoBehaviour
{
    private const int MaximumCharacterCount = 10;
    private const int ButtonsPerRow = 5;
    private const float ButtonWidth = 220f;
    private const float ButtonHeight = 90f;
    private const float HorizontalSpacing = 20f;
    private const float VerticalSpacing = 20f;

    private static readonly string[] DefaultOptionNames =
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

    [Header("Buttons")]
    [SerializeField] private Button[] characterButtons = new Button[10];
    [SerializeField] private Image[] characterButtonImages = new Image[10];
    [SerializeField] private TMP_Text[] characterButtonTexts = new TMP_Text[10];

    [Header("Labels")]
    [SerializeField] private string[] optionNames =
    {
        "Red",
        "Blue",
        "Green"
    };

    [Header("Texts")]
    [SerializeField] private TMP_Text statusText;

    private Color[] characterColors;
    private bool waitingForMatchStart;
    private int lastOccupiedCharactersMask;

    public void Init(Color[] colors)
    {
        EnsureTenCharacterButtons();
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
                string optionName = GetOptionName(i);

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
        SetStatus("Waiting for all players to finish setup");
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

    private void EnsureTenCharacterButtons()
    {
        if (characterButtons == null ||
            characterButtons.Length == 0 ||
            characterButtons[0] == null)
        {
            Debug.LogError(
                "CharacterSelectionUI: assign at least one color button template.",
                this
            );
            return;
        }

        Button buttonTemplate = characterButtons[0];
        TMP_Text textTemplate =
            characterButtonTexts != null && characterButtonTexts.Length > 0
                ? characterButtonTexts[0]
                : null;

        if (textTemplate == null)
        {
            Debug.LogError(
                "CharacterSelectionUI: assign the text of the first color button.",
                this
            );
            return;
        }

        List<Button> buttons = new List<Button>(MaximumCharacterCount);
        List<Image> images = new List<Image>(MaximumCharacterCount);
        List<TMP_Text> texts = new List<TMP_Text>(MaximumCharacterCount);
        int existingCount = Mathf.Min(
            characterButtons.Length,
            MaximumCharacterCount
        );

        for (int i = 0; i < existingCount; i++)
        {
            if (characterButtons[i] == null)
                break;

            buttons.Add(characterButtons[i]);
            images.Add(
                characterButtonImages != null &&
                i < characterButtonImages.Length
                    ? characterButtonImages[i]
                    : characterButtons[i].targetGraphic as Image
            );
            texts.Add(
                characterButtonTexts != null &&
                i < characterButtonTexts.Length
                    ? characterButtonTexts[i]
                    : null
            );
        }

        while (buttons.Count < MaximumCharacterCount)
        {
            int characterIndex = buttons.Count;
            Button button = Instantiate(
                buttonTemplate,
                buttonTemplate.transform.parent
            );
            button.name = $"ColorButton_{characterIndex + 1}";

            for (int childIndex = 0;
                 childIndex < button.transform.childCount;
                 childIndex++)
            {
                button.transform.GetChild(childIndex).gameObject.SetActive(false);
            }

            TMP_Text buttonText = Instantiate(textTemplate, button.transform);
            buttonText.name = "ColorLabel";
            buttonText.gameObject.SetActive(true);
            StretchTextToButton(buttonText);

            buttons.Add(button);
            images.Add(button.targetGraphic as Image);
            texts.Add(buttonText);
        }

        characterButtons = buttons.ToArray();
        characterButtonImages = images.ToArray();
        characterButtonTexts = texts.ToArray();

        LayoutCharacterButtons();
    }

    private void LayoutCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Length; i++)
        {
            if (characterButtons[i] == null)
                continue;

            RectTransform rect =
                characterButtons[i].transform as RectTransform;

            if (rect == null)
                continue;

            int column = i % ButtonsPerRow;
            int row = i / ButtonsPerRow;
            float x =
                (column - (ButtonsPerRow - 1) * 0.5f) *
                (ButtonWidth + HorizontalSpacing);
            float y =
                155f - row * (ButtonHeight + VerticalSpacing);

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            rect.anchoredPosition = new Vector2(x, y);
        }
    }

    private static void StretchTextToButton(TMP_Text buttonText)
    {
        RectTransform rect = buttonText.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private string GetOptionName(int characterIndex)
    {
        if (optionNames != null &&
            characterIndex >= 0 &&
            characterIndex < optionNames.Length &&
            !string.IsNullOrWhiteSpace(optionNames[characterIndex]))
        {
            return optionNames[characterIndex];
        }

        return characterIndex >= 0 &&
               characterIndex < DefaultOptionNames.Length
            ? DefaultOptionNames[characterIndex]
            : $"Color {characterIndex + 1}";
    }
}
