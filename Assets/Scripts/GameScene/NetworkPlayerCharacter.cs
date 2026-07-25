using Fusion;
using UnityEngine;

public class NetworkPlayerCharacter : NetworkBehaviour
{
    [Header("Visual Root")]
    [SerializeField] private GameObject visualsRoot;

    [Header("Avatar Visuals")]
    [SerializeField] private GameObject[] avatarRoots = new GameObject[10];

    [Header("Player Color Accent")]
    [SerializeField] private Renderer[] colorAccentRenderers;

    [Networked, OnChangedRender(nameof(OnCharacterIndexChanged))]
    public int CharacterIndex { get; set; }

    [Networked, OnChangedRender(nameof(OnAvatarIndexChanged))]
    public int AvatarIndex { get; set; } = -1;

    public override void Spawned()
    {
        ApplyCharacterColor();
        ApplyAvatarVisual();
    }

    public void SetVisualsVisible(bool visible)
    {
        if (visualsRoot)
        {
            visualsRoot.SetActive(visible);
            return;
        }

        if (!visible)
        {
            foreach (GameObject avatarRoot in avatarRoots)
            {
                if (avatarRoot)
                    avatarRoot.SetActive(false);
            }
        }
        else
        {
            ApplyAvatarVisual();
        }
    }

    private void OnCharacterIndexChanged()
    {
        ApplyCharacterColor();
    }

    private void OnAvatarIndexChanged()
    {
        ApplyAvatarVisual();
    }

    private void ApplyCharacterColor()
    {
        if (!GameSceneManager.Instance) return;

        Color playerColor = GameSceneManager.Instance.GetCharacterColor(CharacterIndex);

        if (colorAccentRenderers == null) return;

        foreach (Renderer currentRenderer in colorAccentRenderers)
        {
            if (!currentRenderer) continue;
            currentRenderer.material.color = playerColor;
        }
    }

    private void ApplyAvatarVisual()
    {
        if (avatarRoots == null) return;

        for (int i = 0; i < avatarRoots.Length; i++)
        {
            GameObject avatar = avatarRoots[i];

            if (!avatar) continue;

            avatar.SetActive(i == AvatarIndex);
        }
    }
}