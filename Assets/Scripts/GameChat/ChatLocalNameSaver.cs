using TMPro;
using UnityEngine;

public class ChatLocalNameSaver : MonoBehaviour
{
    [SerializeField] private TMP_InputField playerNameInput;

    private void Start()
    {
        SaveName();

        if (playerNameInput != null)
            playerNameInput.onValueChanged.AddListener(_ => SaveName());
    }

    private void OnDestroy()
    {
        if (playerNameInput != null)
            playerNameInput.onValueChanged.RemoveListener(_ => SaveName());
    }

    public void SaveName()
    {
        if (playerNameInput == null)
            return;

        string name = playerNameInput.text.Trim();

        if (string.IsNullOrWhiteSpace(name))
            name = "Player";

        ChatLocalData.PlayerName = name;

        Debug.Log("Chat name saved: " + ChatLocalData.PlayerName);
    }
}