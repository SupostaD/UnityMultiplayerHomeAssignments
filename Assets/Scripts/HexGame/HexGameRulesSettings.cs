using UnityEngine;

[CreateAssetMenu(
    fileName = "HexGameRulesSettings",
    menuName = "Hex Game/Game Rules Settings")]
public sealed class HexGameRulesSettings : ScriptableObject
{
    [Header("Territory Rules")]
    [SerializeField]
    private bool loseStrengthWhenHexIsStolen = true;

    [Header("Player Ball Rules")]
    [SerializeField]
    private bool disablePhysicalRolling = true;

    [Header("Death Zone Rules")]
    [SerializeField, Range(0f, 1f)]
    private float deathZoneStrengthLossFraction = 0.5f;
    [SerializeField, Min(0f)]
    private float deathZoneProtectedStrengthThreshold = 1f;
    [SerializeField]
    private bool roundDeathZoneStrengthLossUp = true;
    [SerializeField, Min(0f)]
    private float deathZoneReturnClearance = 0.1f;

    public bool LoseStrengthWhenHexIsStolen =>
        loseStrengthWhenHexIsStolen;

    public bool DisablePhysicalRolling =>
        disablePhysicalRolling;

    public float DeathZoneStrengthLossFraction =>
        Mathf.Clamp01(deathZoneStrengthLossFraction);

    public float DeathZoneProtectedStrengthThreshold =>
        Mathf.Max(0f, deathZoneProtectedStrengthThreshold);

    public bool RoundDeathZoneStrengthLossUp =>
        roundDeathZoneStrengthLossUp;

    public float DeathZoneReturnClearance =>
        Mathf.Max(0f, deathZoneReturnClearance);
}
