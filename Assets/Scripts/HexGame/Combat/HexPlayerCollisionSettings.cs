using UnityEngine;

[CreateAssetMenu(
    fileName = "HexPlayerCollisionSettings",
    menuName = "Hex Game/Player Collision Settings")]
public sealed class HexPlayerCollisionSettings : ScriptableObject
{
    private const float DefaultReportCollectionSeconds = 0.06f;
    private const float DefaultPendingImpactLifetimeSeconds = 0.25f;
    private const float DefaultMaximumReportedSpeed = 24f;
    private const float DefaultMaximumReportPositionDrift = 4f;
    private const float DefaultPredictionLifetimeSeconds = 1.25f;
    private const float DefaultReconciliationSeconds = 0.12f;
    private const float DefaultMaximumReconciliationVelocityChange = 6f;
    private const float DefaultMaximumRemoteExtrapolationSeconds = 0.12f;
    private const float DefaultCollisionLookAheadSeconds = 0.025f;
    private const float DefaultPredictiveContactSuppressionSeconds = 0.2f;

    [Header("Impact")]
    [SerializeField, Range(0f, 1f)] private float restitution = 0.35f;
    [SerializeField, Min(0f)] private float minimumImpactSpeed = 0.75f;
    [SerializeField, Min(0.1f)] private float maximumVelocityChange = 7f;
    [SerializeField, Min(0.02f)] private float impactCooldownSeconds = 0.18f;

    [Header("Shared Mode Reports")]
    [SerializeField, Min(0.01f)] private float reportCollectionSeconds = 0.06f;
    [SerializeField, Min(0.05f)] private float pendingImpactLifetimeSeconds = 0.25f;
    [SerializeField, Min(0.1f)] private float maximumReportedSpeed = 24f;
    [SerializeField, Min(0.1f)] private float maximumReportPositionDrift = 4f;

    [Header("Shared Mode Prediction")]
    [SerializeField, Min(0.1f)] private float predictionLifetimeSeconds = 1.25f;
    [SerializeField, Min(0.01f)] private float reconciliationSeconds = 0.12f;
    [SerializeField, Min(0.1f)]
    private float maximumReconciliationVelocityChange = 6f;

    [Header("Shared Mode Early Collision Prediction")]
    [SerializeField] private bool enableEarlyCollisionPrediction = true;
    [SerializeField] private bool useNetworkStateAgeForExtrapolation = true;
    [SerializeField, Range(0f, 2f)]
    [Tooltip(
        "Multiplier for the exact age of the remote Fusion state. " +
        "One means extrapolating by the measured state age.")]
    private float networkStateAgeMultiplier = 1f;
    [SerializeField] private bool useRttForRemoteExtrapolation = true;
    [SerializeField, Range(0f, 2f)]
    [Tooltip(
        "Part of the measured RTT used to move a remote player " +
        "forward from its delayed network position.")]
    private float rttExtrapolationMultiplier = 0.75f;
    [SerializeField, Min(0f)]
    [Tooltip(
        "Minimum time used to extrapolate a remote player's position.")]
    private float minimumRemoteExtrapolationSeconds = 0.03f;
    [SerializeField, Min(0.01f)]
    [Tooltip(
        "Maximum extrapolation time. Limits false predictions when RTT spikes.")]
    private float maximumRemoteExtrapolationSeconds = 0.12f;
    [SerializeField, Min(0.01f)]
    [Tooltip(
        "How far into the future the controller searches for a sphere collision.")]
    private float collisionLookAheadSeconds = 0.025f;
    [SerializeField, Min(0f)]
    [Tooltip(
        "Extra distance added to both sphere radii during early prediction.")]
    private float predictionContactPadding = 0.04f;
    [SerializeField, Min(0.02f)]
    [Tooltip(
        "Prevents the real OnCollisionEnter from applying the same predicted hit again.")]
    private float predictiveContactSuppressionSeconds = 0.2f;

    [Header("Player Control After Impact")]
    [SerializeField, Range(0f, 1f)]
    private float controlMultiplierDuringRecovery = 0.3f;
    [SerializeField, Min(0.02f)] private float controlRecoverySeconds = 0.28f;

    [Header("Dash")]
    [SerializeField, Min(1f)] private float dashImpactMultiplier = 1.65f;

    [Header("Vertical Response")]
    [SerializeField, Min(0f)] private float verticalLiftPerImpactSpeed = 0.02f;
    [SerializeField, Min(0f)] private float maximumVerticalLift = 0.35f;

    public float Restitution => Mathf.Clamp01(restitution);
    public float MinimumImpactSpeed => Mathf.Max(0f, minimumImpactSpeed);
    public float MaximumVelocityChange =>
        Mathf.Max(0.1f, maximumVelocityChange);
    public float ImpactCooldownSeconds =>
        Mathf.Max(0.02f, impactCooldownSeconds);
    public float ReportCollectionSeconds =>
        reportCollectionSeconds > 0f
            ? Mathf.Max(0.01f, reportCollectionSeconds)
            : DefaultReportCollectionSeconds;
    public float PendingImpactLifetimeSeconds =>
        Mathf.Max(
            ReportCollectionSeconds,
            pendingImpactLifetimeSeconds > 0f
                ? pendingImpactLifetimeSeconds
                : DefaultPendingImpactLifetimeSeconds
        );
    public float MaximumReportedSpeed =>
        maximumReportedSpeed > 0f
            ? Mathf.Max(0.1f, maximumReportedSpeed)
            : DefaultMaximumReportedSpeed;
    public float MaximumReportPositionDrift =>
        maximumReportPositionDrift > 0f
            ? Mathf.Max(0.1f, maximumReportPositionDrift)
            : DefaultMaximumReportPositionDrift;
    public float PredictionLifetimeSeconds =>
        predictionLifetimeSeconds > 0f
            ? Mathf.Max(0.1f, predictionLifetimeSeconds)
            : DefaultPredictionLifetimeSeconds;
    public float ReconciliationSeconds =>
        reconciliationSeconds > 0f
            ? Mathf.Max(0.01f, reconciliationSeconds)
            : DefaultReconciliationSeconds;
    public float MaximumReconciliationVelocityChange =>
        maximumReconciliationVelocityChange > 0f
            ? Mathf.Max(0.1f, maximumReconciliationVelocityChange)
            : DefaultMaximumReconciliationVelocityChange;
    public bool EnableEarlyCollisionPrediction =>
        enableEarlyCollisionPrediction;
    public bool UseNetworkStateAgeForExtrapolation =>
        useNetworkStateAgeForExtrapolation;
    public float NetworkStateAgeMultiplier =>
        Mathf.Clamp(networkStateAgeMultiplier, 0f, 2f);
    public bool UseRttForRemoteExtrapolation =>
        useRttForRemoteExtrapolation;
    public float RttExtrapolationMultiplier =>
        Mathf.Clamp(rttExtrapolationMultiplier, 0f, 2f);
    public float MinimumRemoteExtrapolationSeconds =>
        Mathf.Max(0f, minimumRemoteExtrapolationSeconds);
    public float MaximumRemoteExtrapolationSeconds =>
        Mathf.Max(
            MinimumRemoteExtrapolationSeconds,
            maximumRemoteExtrapolationSeconds > 0f
                ? maximumRemoteExtrapolationSeconds
                : DefaultMaximumRemoteExtrapolationSeconds
        );
    public float CollisionLookAheadSeconds =>
        collisionLookAheadSeconds > 0f
            ? collisionLookAheadSeconds
            : DefaultCollisionLookAheadSeconds;
    public float PredictionContactPadding =>
        Mathf.Max(0f, predictionContactPadding);
    public float PredictiveContactSuppressionSeconds =>
        predictiveContactSuppressionSeconds > 0f
            ? Mathf.Max(0.02f, predictiveContactSuppressionSeconds)
            : DefaultPredictiveContactSuppressionSeconds;
    public float ControlMultiplierDuringRecovery =>
        Mathf.Clamp01(controlMultiplierDuringRecovery);
    public float ControlRecoverySeconds =>
        Mathf.Max(0.02f, controlRecoverySeconds);
    public float DashImpactMultiplier =>
        Mathf.Max(1f, dashImpactMultiplier);
    public float VerticalLiftPerImpactSpeed =>
        Mathf.Max(0f, verticalLiftPerImpactSpeed);
    public float MaximumVerticalLift =>
        Mathf.Max(0f, maximumVerticalLift);
}
