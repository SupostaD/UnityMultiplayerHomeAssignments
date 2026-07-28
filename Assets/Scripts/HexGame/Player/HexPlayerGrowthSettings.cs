using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(
    fileName = "HexPlayerGrowthSettings",
    menuName = "Hex Game/Player Growth Settings")]
public sealed class HexPlayerGrowthSettings : ScriptableObject
{
    [Header("Strength To Size")]
    [SerializeField, Min(0.01f)] private float minimumUniformScale = 0.25f;
    [SerializeField, Min(0.01f)] private float maximumUniformScale = 1f;
    [FormerlySerializedAs("territoriesForMaximumSize")]
    [SerializeField, Min(0.01f)] private float strengthForMaximumSize = 169f;

    [Header("Movement At Minimum Size")]
    [SerializeField, Min(0.1f)] private float minimumSizeMaximumSpeed = 9f;
    [SerializeField, Min(0.1f)] private float minimumSizeAcceleration = 28f;
    [SerializeField, Min(0.1f)] private float minimumSizeBraking = 34f;

    [Header("Movement At Maximum Size")]
    [SerializeField, Min(0.1f)] private float maximumSizeMaximumSpeed = 4.5f;
    [SerializeField, Min(0.1f)] private float maximumSizeAcceleration = 18f;
    [SerializeField, Min(0.1f)] private float maximumSizeBraking = 25f;

    [Header("Mass")]
    [SerializeField, Min(0.01f)] private float minimumSizeMass = 1f;
    [SerializeField, Min(0f)] private float massScaleExponent = 1f;

    public float EvaluateGrowthProgress(float strength)
    {
        return Mathf.Clamp01(
            Mathf.Max(0f, strength) /
            Mathf.Max(0.01f, strengthForMaximumSize)
        );
    }

    public float EvaluateUniformScale(float strength)
    {
        float minimumScale = Mathf.Max(0.01f, minimumUniformScale);
        float maximumScale = Mathf.Max(minimumScale, maximumUniformScale);

        return Mathf.Lerp(
            minimumScale,
            maximumScale,
            EvaluateGrowthProgress(strength)
        );
    }

    public float EvaluateMaximumSpeed(float strength)
    {
        return Mathf.Lerp(
            Mathf.Max(0.1f, minimumSizeMaximumSpeed),
            Mathf.Max(0.1f, maximumSizeMaximumSpeed),
            EvaluateGrowthProgress(strength)
        );
    }

    public float EvaluateAcceleration(float strength)
    {
        return Mathf.Lerp(
            Mathf.Max(0.1f, minimumSizeAcceleration),
            Mathf.Max(0.1f, maximumSizeAcceleration),
            EvaluateGrowthProgress(strength)
        );
    }

    public float EvaluateBraking(float strength)
    {
        return Mathf.Lerp(
            Mathf.Max(0.1f, minimumSizeBraking),
            Mathf.Max(0.1f, maximumSizeBraking),
            EvaluateGrowthProgress(strength)
        );
    }

    public float EvaluateMass(float strength)
    {
        float minimumScale = Mathf.Max(0.01f, minimumUniformScale);
        float currentScale = EvaluateUniformScale(strength);
        float scaleRatio = currentScale / minimumScale;

        return Mathf.Max(0.01f, minimumSizeMass) *
               Mathf.Pow(scaleRatio, Mathf.Max(0f, massScaleExponent));
    }
}
