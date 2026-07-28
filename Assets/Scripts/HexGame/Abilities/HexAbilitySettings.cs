using UnityEngine;

[CreateAssetMenu(
    fileName = "HexAbilitySettings",
    menuName = "Hex Game/Ability Settings")]
public sealed class HexAbilitySettings : ScriptableObject
{
    [Header("Player Icon Animation")]
    [SerializeField, Min(0.01f)] private float iconTransitionSeconds = 0.2f;
    [SerializeField, Range(0f, 1f)] private float hiddenIconScale = 0.75f;

    [Header("Pickup")]
    [SerializeField, Min(0f)] private float pickupRespawnSeconds = 10f;
    [SerializeField, Min(0.1f)] private float pickupValidationDistance = 3f;
    [SerializeField, Min(0.1f)] private float reservationTimeoutSeconds = 1f;

    public float IconTransitionSeconds =>
        Mathf.Max(0.01f, iconTransitionSeconds);

    public float HiddenIconScale =>
        Mathf.Clamp01(hiddenIconScale);

    public float PickupRespawnSeconds =>
        Mathf.Max(0f, pickupRespawnSeconds);

    public float PickupValidationDistance =>
        Mathf.Max(0.1f, pickupValidationDistance);

    public float ReservationTimeoutSeconds =>
        Mathf.Max(0.1f, reservationTimeoutSeconds);
}
