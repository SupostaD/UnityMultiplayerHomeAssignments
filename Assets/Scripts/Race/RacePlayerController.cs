using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class RacePlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private RaceMovementSettings movementSettings;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheckOrigin;

    [Header("Runtime")]
    [SerializeField] private bool startOnTrack = true;

    [Networked] public int CurrentLap { get; private set; }
    [Networked] public int NextCheckpointIndex { get; private set; }
    [Networked] public NetworkBool HasFinished { get; private set; }
    [Networked] public int FinishTick { get; private set; }
    [Networked] private float CurrentSpeed { get; set; }

    private bool isOnTrack;
    private bool isGrounded;
    private float verticalVelocity;
    private Vector3 groundNormal = Vector3.up;
    private Rigidbody cachedRigidbody;
    private Collider cachedCollider;

    public float Speed => CurrentSpeed;
    public bool IsGrounded => isGrounded;

    private void Awake()
    {
        ConfigureRigidbody();
    }

    private void Reset()
    {
        ConfigureRigidbody();
    }

    public override void Spawned()
    {
        isOnTrack = startOnTrack;

        if (Object.HasStateAuthority)
            ResetRaceProgress();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        RaceMovementSettings settings = GetSettings();
        float deltaTime = Runner.DeltaTime;

        UpdateGrounding(settings, deltaTime);

        if (!CanMove())
        {
            CurrentSpeed = Mathf.MoveTowards(
                CurrentSpeed,
                0f,
                settings.brakeDeceleration * deltaTime
            );
            return;
        }

        if (!GetInput(out RaceInputData input))
            input = default;

        Move(input, settings, deltaTime);
    }

    public void SetOnTrack(bool value)
    {
        isOnTrack = value;
    }

    public void ReachedCheckpoint(RaceCheckpoint checkpoint)
    {
        if (checkpoint == null)
            return;

        if (!Object.HasStateAuthority)
            return;

        RaceGameManager raceGameManager = RaceGameManager.Instance;

        if (raceGameManager == null || !raceGameManager.IsRaceInProgress)
            return;

        if (HasFinished)
            return;

        int checkpointIndex = checkpoint.CheckpointIndex;

        if (checkpointIndex != NextCheckpointIndex)
            return;

        int checkpointCount = Mathf.Max(1, raceGameManager.CheckpointCount);

        if (checkpointIndex == 0)
        {
            CurrentLap++;

            if (CurrentLap >= raceGameManager.TotalLaps)
            {
                HasFinished = true;
                FinishTick = Runner.Tick;
                CurrentSpeed = 0f;
                return;
            }

            NextCheckpointIndex = checkpointCount > 1 ? 1 : 0;
            return;
        }

        NextCheckpointIndex = checkpointIndex >= checkpointCount - 1
            ? 0
            : checkpointIndex + 1;
    }

    public void ResetRaceProgress()
    {
        CurrentLap = 0;
        HasFinished = false;
        FinishTick = 0;
        CurrentSpeed = 0f;

        int checkpointCount = RaceGameManager.Instance != null
            ? RaceGameManager.Instance.CheckpointCount
            : 0;

        NextCheckpointIndex = checkpointCount > 1 ? 1 : 0;
    }

    private bool CanMove()
    {
        if (GameInputBlocker.IsGameplayInputBlocked)
            return false;

        if (HasFinished)
            return false;

        return RaceGameManager.Instance != null && RaceGameManager.Instance.CanPlayersMove;
    }

    private void Move(RaceInputData input, RaceMovementSettings settings, float deltaTime)
    {
        float throttle = Mathf.Clamp01(input.Throttle);
        float brake = Mathf.Clamp01(input.Brake);
        bool handbrake = input.Buttons.IsSet(RaceInputButton.Handbrake);

        float desiredSpeed = 0f;

        if (throttle > 0.01f)
            desiredSpeed = settings.maxForwardSpeed * throttle;
        else if (brake > 0.01f)
            desiredSpeed = -settings.maxReverseSpeed * brake;

        float speedChangeRate = GetSpeedChangeRate(settings, desiredSpeed, handbrake);
        float controlMultiplier = isGrounded ? 1f : settings.airControlMultiplier;

        CurrentSpeed = Mathf.MoveTowards(
            CurrentSpeed,
            desiredSpeed,
            speedChangeRate * controlMultiplier * deltaTime
        );

        float trackSpeedMultiplier = isOnTrack ? 1f : settings.offTrackSpeedMultiplier;
        float effectiveSpeed = CurrentSpeed * trackSpeedMultiplier;
        float speedRatio = Mathf.Clamp01(Mathf.Abs(effectiveSpeed) / Mathf.Max(settings.maxForwardSpeed, 0.01f));
        float turnFactor = Mathf.Lerp(settings.minTurnSpeedFactor, 1f, speedRatio);
        float turnMultiplier = handbrake ? settings.handbrakeTurnMultiplier : 1f;
        float turnAmount = input.Steering * settings.turnSpeed * turnFactor * turnMultiplier * controlMultiplier * deltaTime;

        if (Mathf.Abs(effectiveSpeed) > 0.05f)
        {
            float direction = Mathf.Sign(effectiveSpeed);
            transform.Rotate(0f, turnAmount * direction, 0f, Space.Self);
        }

        if (isGrounded && settings.alignToGround)
            AlignToGround(settings, deltaTime);

        Vector3 movementDirection = GetMovementDirection();
        transform.position += movementDirection * (effectiveSpeed * deltaTime);
    }

    private void ConfigureRigidbody()
    {
        cachedRigidbody = GetComponent<Rigidbody>();
        cachedCollider = GetComponent<Collider>();

        if (cachedRigidbody == null)
            return;

        cachedRigidbody.isKinematic = true;
        cachedRigidbody.useGravity = false;
        cachedRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void UpdateGrounding(RaceMovementSettings settings, float deltaTime)
    {
        Vector3 checkPosition = groundCheckOrigin != null
            ? groundCheckOrigin.position
            : transform.position;

        Vector3 rayOrigin = checkPosition + Vector3.up * settings.groundCheckHeight;
        float rayDistance = settings.groundCheckHeight + settings.groundCheckDistance;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                rayDistance,
                settings.groundMask,
                QueryTriggerInteraction.Ignore))
        {
            isGrounded = true;
            groundNormal = hit.normal;
            verticalVelocity = 0f;

            float targetY = hit.point.y + GetGroundHeightOffset(settings);
            Vector3 position = transform.position;
            position.y = Mathf.MoveTowards(position.y, targetY, settings.groundSnapSpeed * deltaTime);
            transform.position = position;

            return;
        }

        isGrounded = false;
        groundNormal = Vector3.up;
        verticalVelocity += settings.gravity * deltaTime;
        transform.position += Vector3.down * (verticalVelocity * deltaTime);
    }

    private float GetGroundHeightOffset(RaceMovementSettings settings)
    {
        if (settings.groundHeightOffset > 0f)
            return settings.groundHeightOffset;

        if (cachedCollider == null)
            cachedCollider = GetComponent<Collider>();

        if (cachedCollider == null)
            return 0f;

        return Mathf.Max(0f, transform.position.y - cachedCollider.bounds.min.y);
    }

    private Vector3 GetMovementDirection()
    {
        if (!isGrounded)
            return transform.forward;

        Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, groundNormal);

        return projectedForward.sqrMagnitude > 0.0001f
            ? projectedForward.normalized
            : transform.forward;
    }

    private void AlignToGround(RaceMovementSettings settings, float deltaTime)
    {
        Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, groundNormal);

        if (projectedForward.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(projectedForward.normalized, groundNormal);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            settings.groundAlignSpeed * deltaTime
        );
    }

    private float GetSpeedChangeRate(RaceMovementSettings settings, float desiredSpeed, bool handbrake)
    {
        if (handbrake)
            return settings.handbrakeDeceleration;

        if (Mathf.Abs(desiredSpeed) < 0.01f)
            return settings.coastDeceleration;

        bool acceleratingInSameDirection =
            Mathf.Sign(desiredSpeed) == Mathf.Sign(CurrentSpeed) &&
            Mathf.Abs(desiredSpeed) > Mathf.Abs(CurrentSpeed);

        return acceleratingInSameDirection ? settings.acceleration : settings.brakeDeceleration;
    }

    private RaceMovementSettings GetSettings()
    {
        if (movementSettings != null)
            return movementSettings;

        Debug.LogWarning("RaceMovementSettings is not assigned. Using temporary defaults.");
        movementSettings = ScriptableObject.CreateInstance<RaceMovementSettings>();
        return movementSettings;
    }
}
