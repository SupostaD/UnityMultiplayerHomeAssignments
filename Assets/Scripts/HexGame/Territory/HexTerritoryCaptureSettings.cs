using UnityEngine;

[CreateAssetMenu(
    fileName = "HexTerritoryCaptureSettings",
    menuName = "Hex Game/Territory Capture Settings")]
public sealed class HexTerritoryCaptureSettings : ScriptableObject
{
    [Header("Capture Visual")]
    [Tooltip("How many seconds one hex takes to fill with the new player color.")]
    [SerializeField, Min(0.01f)]
    private float captureAnimationDuration = 0.45f;

    [Tooltip("The hex outer radius in the renderer's local object space. The current ProBuilder hex uses 0.5.")]
    [SerializeField, Min(0.01f)]
    private float visualHexRadius = 0.5f;

    [Header("Enclosed Area Capture")]
    [SerializeField, Min(0f)]
    private float enclosedHexStrengthMultiplier = 1f;

    public float CaptureAnimationDuration =>
        Mathf.Max(0.01f, captureAnimationDuration);

    public float VisualHexRadius =>
        Mathf.Max(0.01f, visualHexRadius);

    public float EnclosedHexStrengthMultiplier =>
        Mathf.Max(0f, enclosedHexStrengthMultiplier);
}
