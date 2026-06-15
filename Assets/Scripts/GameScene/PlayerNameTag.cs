using Fusion;
using TMPro;
using UnityEngine;

public class PlayerBonusNameTag : NetworkBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private bool faceCamera = true;

    private Camera mainCamera;

    public override void Spawned()
    {
        mainCamera = Camera.main;
        UpdateNameTag();
    }

    public override void Render()
    {
        UpdateNameTag();
        FaceCamera();
    }

    private void UpdateNameTag()
    {
        if (nameText == null)
            return;

        string playerName = "Player " + Object.InputAuthority.PlayerId;
        Color playerColor = Color.white;

        if (GameChatNetwork.Instance != null)
        {
            playerName = GameChatNetwork.Instance.GetPlayerName(Object.InputAuthority);
            playerColor = GameChatNetwork.Instance.GetPlayerColor(Object.InputAuthority);
        }

        nameText.text = playerName;
        nameText.color = playerColor;
    }

    private void FaceCamera()
    {
        if (!faceCamera)
            return;

        if (nameText == null)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return;

        nameText.transform.rotation = mainCamera.transform.rotation;
    }
}