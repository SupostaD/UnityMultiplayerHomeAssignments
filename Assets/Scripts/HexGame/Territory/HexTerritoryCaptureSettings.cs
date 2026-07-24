using UnityEngine;

[CreateAssetMenu(
    fileName = "HexTerritoryCaptureSettings",
    menuName = "Hex Game/Territory Capture Settings")]
public sealed class HexTerritoryCaptureSettings : ScriptableObject
{
    [Header("Enclosed Area Capture")]
    [SerializeField, Min(0f)]
    private float enclosedHexStrengthMultiplier = 1f;

    public float EnclosedHexStrengthMultiplier =>
        Mathf.Max(0f, enclosedHexStrengthMultiplier);
}
