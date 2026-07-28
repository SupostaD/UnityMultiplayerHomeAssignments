using UnityEngine;

[CreateAssetMenu(
    fileName = "HexStrengthCombatSettings",
    menuName = "Hex Game/Strength Combat Settings")]
public sealed class HexStrengthCombatSettings : ScriptableObject
{
    [Header("Immediate Contact")]
    [SerializeField, Min(0f)]
    [Tooltip(
        "Strength transferred immediately when two players begin touching. " +
        "Set to 0 to disable the initial instant transfer.")]
    private float initialContactDrain = 1f;

    [Header("Continuous Drain")]
    [SerializeField, Min(0.01f)]
    [Tooltip(
        "Strength transferred from the weaker player to the stronger player " +
        "per second while they remain in contact.")]
    private float strengthDrainPerSecond = 6f;

    [SerializeField, Min(0.02f)]
    [Tooltip(
        "How often the authoritative player applies continuous strength drain. " +
        "Lower values make the drain update more smoothly.")]
    private float transferIntervalSeconds = 0.05f;

    [SerializeField, Min(0.02f)]
    [Tooltip(
        "How often each player reports an ongoing physical contact over the network.")]
    private float contactReportIntervalSeconds = 0.05f;

    [SerializeField, Min(0.05f)]
    [Tooltip(
        "How long a contact remains active without receiving another report. " +
        "This should be longer than the report interval.")]
    private float contactMemorySeconds = 0.2f;

    [Header("Authority Validation")]
    [SerializeField, Min(0.1f)]
    [Tooltip(
        "Fallback maximum distance at which the authority accepts that two " +
        "players are touching. Their combined collision radii can increase it.")]
    private float maximumContactDistance = 2.25f;

    public float InitialContactDrain =>
        Mathf.Max(0f, initialContactDrain);
    public float StrengthDrainPerSecond =>
        Mathf.Max(0.01f, strengthDrainPerSecond);
    public float TransferIntervalSeconds =>
        Mathf.Max(0.02f, transferIntervalSeconds);
    public float ContactReportIntervalSeconds =>
        Mathf.Max(0.02f, contactReportIntervalSeconds);
    public float ContactMemorySeconds =>
        Mathf.Max(
            Mathf.Max(0.05f, contactMemorySeconds),
            ContactReportIntervalSeconds * 2f
        );
    public float MaximumContactDistance =>
        Mathf.Max(0.1f, maximumContactDistance);
}
