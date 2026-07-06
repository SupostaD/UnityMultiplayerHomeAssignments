using System.Collections;
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
    [SerializeField, Min(0f)] private float unexpectedPositionResetDistance = 25f;

    [Header("Trap / Projectile")]
    [SerializeField] private NetworkObject trapPrefab;
    [SerializeField] private Transform trapSpawnPoint;
    [SerializeField, Min(0f)] private float trapSpawnDistanceBehindPlayer = 2f;
    [SerializeField, Min(0f)] private float trapUseCooldownSeconds = 0.35f;
    [SerializeField, Min(0f)] private float trapHitValidationDistance = 3.5f;
    [SerializeField, Min(0f)] private float trapStunSeconds = 1.25f;
    [SerializeField] private bool startWithTrap;
    
    [Header("Local Hit Feedback")]
    [SerializeField] private GameObject hitVisualEffectPrefab;
    [SerializeField] private AudioSource hitAudioSource;
    [SerializeField] private Renderer[] hitFlashRenderers;
    [SerializeField] private Color hitFlashColor = Color.red;
    [SerializeField, Min(0f)] private float hitFlashSeconds = 0.15f;
    
    [Networked] public int CurrentLap { get; private set; }
    [Networked] public int NextCheckpointIndex { get; private set; }
    [Networked] public NetworkBool HasFinished { get; private set; }
    [Networked] public int FinishTick { get; private set; }
    [Networked] public NetworkBool HasTrap { get; private set; }
    [Networked] public int ReceivedTrapHits { get; private set; }
    
    [Networked, OnChangedRender(nameof(OnTrapHitCounterChanged))]
    public int TrapHitCounter { get; private set; }
    [Networked] private float CurrentSpeed { get; set; }
    [Networked] private TickTimer TrapUseCooldown { get; set; }
    [Networked] private TickTimer StunTimer { get; set; }
    [Networked] private Vector3 NetworkedPosition { get; set; }
    [Networked] private Quaternion NetworkedRotation { get; set; }
    [Networked] private NetworkBool HasNetworkedTransform { get; set; }

    private bool isOnTrack;
    private bool isGrounded;
    private float verticalVelocity;
    private Vector3 groundNormal = Vector3.up;
    private Rigidbody cachedRigidbody;
    private Collider cachedCollider;
    private Collider[] ownColliders;
    private NetworkTransform cachedNetworkTransform;
    private NetworkButtons previousButtons;
    private Coroutine hitFlashRoutine;
    private int requiredLoopResult;

    public float Speed => CurrentSpeed;
    public bool IsGrounded => isGrounded;
    public bool IsStunned => Runner && !StunTimer.ExpiredOrNotRunning(Runner);

    private void Awake()
    {
        ConfigureRigidbody();
        DisableNetworkTransformForRace();
    }

    private void Reset()
    {
        ConfigureRigidbody();
    }

    public override void Spawned()
    {
        DisableNetworkTransformForRace();
        isOnTrack = startOnTrack;

        Debug.Log(
            "RacePlayerController Spawned | Root position: " +
            transform.position +
            " | Has StateAuthority: " +
            Object.HasStateAuthority +
            " | Has InputAuthority: " +
            Object.HasInputAuthority
        );

        Transform visuals = transform.Find("Visuals");

        if (visuals != null)
        {
            Debug.Log(
                "Visuals local position: " +
                visuals.localPosition +
                " | Visuals world position: " +
                visuals.position
            );
        }

        if (Object.HasStateAuthority)
        {
            WriteNetworkedTransform();
            ResetRaceProgress();
            HasTrap = startWithTrap;
        }
        else
        {
            ApplyNetworkedTransform();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        RaceMovementSettings settings = GetSettings();
        float deltaTime = Runner.DeltaTime;

        RepairUnexpectedTransformReset();
        UpdateGrounding(settings, deltaTime);

        if (!GetInput(out RaceInputData input))
            input = default;

        HandleTrapInput(input);
        previousButtons = input.Buttons;
        
        if (!CanMove())
        {
            CurrentSpeed = Mathf.MoveTowards(
                CurrentSpeed,
                0f,
                settings.brakeDeceleration * deltaTime
            );
            WriteNetworkedTransform();
            return;
        }

        Move(input, settings, deltaTime);
        WriteNetworkedTransform();
    }

    public override void Render()
    {
        if (Object.HasStateAuthority)
            return;

        ApplyNetworkedTransform();
    }

    public void InitializeSpawnTransform(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        DisableNetworkTransformForRace();
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        NetworkedPosition = spawnPosition;
        NetworkedRotation = spawnRotation;
        HasNetworkedTransform = true;
    }

    public void SetOnTrack(bool value)
    {
        isOnTrack = value;
    }

    public bool TryGrantTrap()
    {
        if (!Object.HasStateAuthority)
            return false;

        if (HasTrap)
            return false;

        HasTrap = true;
        return true;
    }
    
    public bool TryReceiveTrapHit(PlayerRef trapOwner, Vector3 trapPosition)
    {
        if (!CanAcceptTrapHit(trapOwner, trapPosition))
            return false;

        RPC_RequestTrapHit(trapOwner, trapPosition);
        return true;
    }
    
    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestTrapHit(PlayerRef trapOwner, Vector3 trapPosition, RpcInfo info = default)
    {
        if (!CanAcceptTrapHit(trapOwner, trapPosition))
            return;

        ApplyTrapHit(trapOwner);
    }

    private bool CanAcceptTrapHit(PlayerRef trapOwner, Vector3 trapPosition)
    {
        if (!Object.HasStateAuthority)
            return false;

        if (trapOwner == Object.InputAuthority)
            return false;

        if (HasFinished)
            return false;

        if (RaceGameManager.Instance && !RaceGameManager.Instance.IsRaceInProgress)
            return false;

        float maxDistance = Mathf.Max(0.1f, trapHitValidationDistance);
        float sqrDistance = (transform.position - trapPosition).sqrMagnitude;

        return sqrDistance <= maxDistance * maxDistance;
    }

    private void ApplyTrapHit(PlayerRef trapOwner)
    {
        ReceivedTrapHits++;
        TrapHitCounter++;
        CurrentSpeed = 0f;
        StunTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.05f, trapStunSeconds));
    }
    
    public void ReachedCheckpoint(RaceCheckpoint checkpoint)
    {
        if (!checkpoint)
            return;

        if (!Object.HasStateAuthority)
            return;

        RaceGameManager raceGameManager = RaceGameManager.Instance;

        if (!raceGameManager || !raceGameManager.IsRaceInProgress)
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
        ReceivedTrapHits = 0;
        TrapHitCounter = 0;
        StunTimer = default;
        TrapUseCooldown = default;

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
        
        if (IsStunned)
            return false;

        return RaceGameManager.Instance && RaceGameManager.Instance.CanPlayersMove;
    }
    
    private void HandleTrapInput(RaceInputData input)
    {
        bool useTrapPressed = input.Buttons.WasPressed(previousButtons, RaceInputButton.UseTrap);

        if (!useTrapPressed)
            return;

        TryUseTrap();
    }
    
    private bool TryUseTrap()
    {
        if (!HasTrap)
            return false;

        if (trapPrefab == null)
            return false;

        if (RaceGameManager.Instance != null && !RaceGameManager.Instance.IsRaceInProgress)
            return false;

        if (!TrapUseCooldown.ExpiredOrNotRunning(Runner))
            return false;

        Vector3 spawnPosition = GetTrapSpawnPosition();
        Quaternion spawnRotation = trapSpawnPoint != null ? trapSpawnPoint.rotation : transform.rotation;

        NetworkObject spawnedTrap = Runner.Spawn(
            trapPrefab,
            spawnPosition,
            spawnRotation,
            Object.InputAuthority
        );

        if (spawnedTrap == null)
            return false;

        RaceTrap raceTrap = spawnedTrap.GetComponent<RaceTrap>();

        if (raceTrap != null)
            raceTrap.Initialize(Object.InputAuthority);

        HasTrap = false;
        TrapUseCooldown = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.01f, trapUseCooldownSeconds));
        return true;
    }
    
    private Vector3 GetTrapSpawnPosition()
    {
        if (trapSpawnPoint != null)
            return trapSpawnPoint.position;

        Vector3 behindDirection = isGrounded
            ? -GetMovementDirection()
            : -transform.forward;

        if (behindDirection.sqrMagnitude <= 0.0001f)
            behindDirection = -transform.forward;

        return transform.position + behindDirection.normalized * trapSpawnDistanceBehindPlayer;
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
        ownColliders = GetComponentsInChildren<Collider>();

        if (!cachedRigidbody)
            return;

        cachedRigidbody.isKinematic = true;
        cachedRigidbody.useGravity = false;
        cachedRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void DisableNetworkTransformForRace()
    {
        if (!cachedNetworkTransform)
            cachedNetworkTransform = GetComponent<NetworkTransform>();

        if (cachedNetworkTransform && cachedNetworkTransform.enabled)
            cachedNetworkTransform.enabled = false;
    }

    private void WriteNetworkedTransform()
    {
        NetworkedPosition = transform.position;
        NetworkedRotation = transform.rotation;
        HasNetworkedTransform = true;
    }

    private void ApplyNetworkedTransform()
    {
        if (!HasNetworkedTransform)
            return;

        transform.SetPositionAndRotation(NetworkedPosition, NetworkedRotation);
    }

    private void RepairUnexpectedTransformReset()
    {
        if (!HasNetworkedTransform)
            return;

        Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 expectedXZ = new Vector2(NetworkedPosition.x, NetworkedPosition.z);
        float maxDistance = Mathf.Max(1f, unexpectedPositionResetDistance);

        if ((currentXZ - expectedXZ).sqrMagnitude <= maxDistance * maxDistance)
            return;

        Debug.LogWarning(
            "RacePlayerController repaired unexpected transform reset. Current: " +
            transform.position +
            " | Expected: " +
            NetworkedPosition
        );

        transform.SetPositionAndRotation(NetworkedPosition, NetworkedRotation);
    }

    private void UpdateGrounding(RaceMovementSettings settings, float deltaTime)
    {
        Vector3 checkPosition = groundCheckOrigin != null
            ? groundCheckOrigin.position
            : transform.position;

        Vector3 rayOrigin = checkPosition + Vector3.up * settings.groundCheckHeight;
        float rayDistance = settings.groundCheckHeight + settings.groundCheckDistance;

        if (TryFindGroundHit(rayOrigin, rayDistance, settings.groundMask, out RaycastHit hit))
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

    private bool TryFindGroundHit(Vector3 rayOrigin, float rayDistance, LayerMask groundMask, out RaycastHit bestHit)
    {
        bestHit = default;
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            rayDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.MaxValue;
        bool foundGround = false;

        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider)
                continue;

            if (IsOwnCollider(hit.collider))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            bestHit = hit;
            foundGround = true;
        }

        return foundGround;
    }

    private bool IsOwnCollider(Collider candidate)
    {
        if (!candidate)
            return false;

        if (ownColliders == null || ownColliders.Length == 0)
            ownColliders = GetComponentsInChildren<Collider>();

        foreach (Collider ownCollider in ownColliders)
        {
            if (ownCollider == candidate)
                return true;
        }

        return false;
    }

    private float GetGroundHeightOffset(RaceMovementSettings settings)
    {
        if (settings.groundHeightOffset > 0f)
            return settings.groundHeightOffset;

        if (!cachedCollider)
            cachedCollider = GetComponent<Collider>();

        if (!cachedCollider)
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
        if (movementSettings)
            return movementSettings;

        Debug.LogWarning("RaceMovementSettings is not assigned. Using temporary defaults.");
        movementSettings = ScriptableObject.CreateInstance<RaceMovementSettings>();
        return movementSettings;
    }
    
    private void OnTrapHitCounterChanged()
    {
        RunRequiredCountingLoops();

        if (!Object.HasInputAuthority)
            return;

        PlayLocalTrapHitFeedback();
    }

    private void RunRequiredCountingLoops()
    {
        int firstCounter = 0;
        int secondCounter = 0;
        int thirdCounter = 0;

        for (int i = 0; i < 1000; i++)
            firstCounter = i;

        for (int i = 0; i < 1000; i++)
            secondCounter = i;

        for (int i = 0; i < 1000; i++)
            thirdCounter = i;

        requiredLoopResult = firstCounter + secondCounter + thirdCounter;
    }

    private void PlayLocalTrapHitFeedback()
    {
        if (hitVisualEffectPrefab)
            Instantiate(hitVisualEffectPrefab, transform.position, Quaternion.identity);

        if (hitAudioSource)
            hitAudioSource.Play();

        if (hitFlashRenderers == null || hitFlashRenderers.Length == 0)
            return;

        if (hitFlashRoutine != null)
            StopCoroutine(hitFlashRoutine);

        hitFlashRoutine = StartCoroutine(FlashAfterTrapHit());
    }

    private IEnumerator FlashAfterTrapHit()
    {
        Color[] originalColors = new Color[hitFlashRenderers.Length];

        for (int i = 0; i < hitFlashRenderers.Length; i++)
        {
            Renderer currentRenderer = hitFlashRenderers[i];

            if (!currentRenderer || !currentRenderer.material)
                continue;

            originalColors[i] = currentRenderer.material.color;
            currentRenderer.material.color = hitFlashColor;
        }

        yield return new WaitForSeconds(hitFlashSeconds);

        for (int i = 0; i < hitFlashRenderers.Length; i++)
        {
            Renderer currentRenderer = hitFlashRenderers[i];

            if (!currentRenderer || !currentRenderer.material)
                continue;

            currentRenderer.material.color = originalColors[i];
        }

        hitFlashRoutine = null;
    }
}
