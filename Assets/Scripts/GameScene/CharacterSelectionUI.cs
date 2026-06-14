using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectionUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button[] characterButtons = new Button[10];

    [Header("Texts")]
    [SerializeField] private TMP_Text statusText;

    private Color[] characterColors;

    public void Init(Color[] colors)
    {
        characterColors = colors;

        for (int i = 0; i < characterButtons.Length; i++)
        {
            int characterIndex = i;

            if (!characterButtons[i]) continue;

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
        for (int i = 0; i < characterButtons.Length; i++)
        {
            if (!characterButtons[i]) continue;

            bool isOccupied = (occupiedCharactersMask & (1 << i)) != 0;

            characterButtons[i].interactable = !isOccupied;

            TMP_Text buttonText = characterButtons[i].GetComponentInChildren<TMP_Text>();

            if (buttonText)
            {
                buttonText.text = isOccupied
                    ? $"Character {i + 1}\nOccupied"
                    : $"Character {i + 1}\nFree";
            }
        }
    }

    public void SetStatus(string message)
    {
        Debug.Log(message);

        if (statusText) statusText.text = message;
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
        Image image = button.GetComponent<Image>();

        if (!image) return;

        if (characterColors == null) return;

        if (characterIndex < 0 || characterIndex >= characterColors.Length) 
            return;

        image.color = characterColors[characterIndex];
    }
}