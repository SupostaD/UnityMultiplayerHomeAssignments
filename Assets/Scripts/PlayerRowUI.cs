using Fusion;
using TMPro;
using UnityEngine;

public class PlayerRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text playerText;

    public void Init(string playerName, PlayerRef player, bool isLocal, bool isMaster)
    {
        string text = playerName;

        if (string.IsNullOrWhiteSpace(text))
            text = "Player " + player.PlayerId;

        if (isLocal)
            text += " (You)";

        if (isMaster)
            text += " [Master]";

        playerText.text = text;
    }
}
