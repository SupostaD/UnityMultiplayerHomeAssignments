using Fusion;
using TMPro;
using UnityEngine;

public class PlayerBonusNameTag : NetworkBehaviour
{
    [Header("Required References")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private HexBallPlayerController playerController;

    [Header("Presentation")]
    [SerializeField] private bool faceCamera = true;

    private Camera mainCamera;
    private Vector3 nameTagLocalOffset;
    private bool nameTagOffsetInitialized;

    public override void Spawned()
    {
        if (nameText == null)
        {
            Debug.LogError(
                "PlayerBonusNameTag: Name Text is not assigned.",
                this
            );
        }

        if (playerController == null)
        {
            Debug.LogError(
                "PlayerBonusNameTag: Player Controller is not assigned.",
                this
            );
        }

        mainCamera = Camera.main;

        if (nameText != null)
        {
            nameTagLocalOffset = nameText.transform.localPosition;
            nameTagOffsetInitialized = true;
        }

        UpdateNameTag();
        UpdateNameTagPose();
    }

    public override void Render()
    {
        UpdateNameTag();
    }

    private void LateUpdate()
    {
        UpdateNameTagPose();
    }

    private void UpdateNameTag()
    {
        if (nameText == null)
            return;

        string playerName = "Player " + Object.InputAuthority.PlayerId;

        if (GameChatNetwork.Instance != null)
            playerName = GameChatNetwork.Instance.GetPlayerName(Object.InputAuthority);

        int displayedStrength = playerController != null
            ? playerController.DisplayedStrength
            : 0;

        nameText.text = $"{playerName} {displayedStrength}";
        nameText.color = Color.black;
    }

    private void UpdateNameTagPose()
    {
        if (nameText == null || !nameTagOffsetInitialized)
            return;

        Vector3 rootScale = transform.lossyScale;
        Vector3 worldOffset = new Vector3(
            nameTagLocalOffset.x * Mathf.Abs(rootScale.x),
            nameTagLocalOffset.y * Mathf.Abs(rootScale.y),
            nameTagLocalOffset.z * Mathf.Abs(rootScale.z)
        );

        nameText.transform.position = transform.position + worldOffset;

        if (!faceCamera)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return;

        nameText.transform.rotation = mainCamera.transform.rotation;
    }
}
