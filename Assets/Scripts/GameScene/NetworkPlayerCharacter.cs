using Fusion;
using UnityEngine;

public class NetworkPlayerCharacter : NetworkBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Renderer[] renderers;

    [Networked, OnChangedRender(nameof(OnCharacterIndexChanged))]
    public int CharacterIndex { get; set; }

    public override void Spawned()
    {
        ApplyCharacterColor();
    }

    public void SetVisualsVisible(bool visible)
    {
        if (renderers == null)
            return;

        foreach (Renderer currentRenderer in renderers)
        {
            if (currentRenderer != null)
                currentRenderer.enabled = visible;
        }
    }

    private void OnCharacterIndexChanged()
    {
        ApplyCharacterColor();
    }

    private void ApplyCharacterColor()
    {
        if (!GameSceneManager.Instance) return;

        Color color = GameSceneManager.Instance.GetCharacterColor(CharacterIndex);

        if (renderers == null)
            return;

        foreach (Renderer currentRenderer in renderers)
        {
            if (!currentRenderer) continue;

            currentRenderer.material.color = color;
        }
    }
}
