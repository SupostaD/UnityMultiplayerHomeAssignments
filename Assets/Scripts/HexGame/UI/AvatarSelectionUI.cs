using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AvatarSelectionUI : MonoBehaviour
{
    private const int AvatarCount = 10;

    [Header("Buttons")]
    [SerializeField] private Button[] avatarButtons = new Button[AvatarCount];
    [SerializeField] private TMP_Text[] avatarButtonTexts = new TMP_Text[AvatarCount];

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;

    private readonly string[] avatarNames =
    {
        "Football",
        "Basketball",
        "Baseball",
        "Bowling Ball",
        "8-Ball",
        "Golf Ball",
        "Beach Ball",
        "Tennis Ball",
        "Volleyball",
        "Dirt Ball"
    };

    private AvatarSelectionManager manager;

    public void Init(AvatarSelectionManager selectionManager)
    {
        manager = selectionManager;

        for (int i = 0; i < avatarButtons.Length; i++)
        {
            Button button = avatarButtons[i];

            if (!button) continue;

            int avatarIndex = i;

            button.onClick.RemoveAllListeners();

            button.onClick.AddListener(() => manager.RequestAvatar(avatarIndex));
        }
    }

    public void Refresh(int occupiedMask, int localAvatarIndex)
    {
        for (int i = 0; i < avatarButtons.Length; i++)
        {
            Button button = avatarButtons[i];

            if (!button) continue;

            bool occupied = (occupiedMask & (1 << i)) != 0;

            bool selectedByLocalPlayer = localAvatarIndex == i;

            button.interactable = localAvatarIndex < 0 && !occupied;

            TMP_Text buttonText = avatarButtonTexts != null && i < avatarButtonTexts.Length
                    ? avatarButtonTexts[i]
                    : null;

            if (!buttonText) continue;

            string avatarName = i < avatarNames.Length
                    ? avatarNames[i]
                    : $"Avatar {i + 1}";

            if (selectedByLocalPlayer)
                buttonText.text = $"{avatarName}\nSELECTED";
            else if (occupied)
                buttonText.text = $"{avatarName}\nTAKEN";
            else
                buttonText.text = avatarName;
        }
    }

    public void SetStatus(string message)
    {
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
}