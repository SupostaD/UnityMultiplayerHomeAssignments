using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class RacePlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private RaceMovementSettings movementSettings;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheckOrigin;

    [Header("Required References")]
    [SerializeField] private Rigidbody cachedRigidbody;
    [SerializeField] private Collider cachedCollider;
    [SerializeField] private Collider[] ownColliders;
    [SerializeField] private NetworkTransform cachedNetworkTransform;
    [SerializeField] private NetworkPlayerCharacter playerCharacter;

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
    private Collider currentGroundCollider;
    private NetworkButtons previousButtons;
    private Coroutine hitFlashRoutine;
    private int requiredLoopResult;

    private static readonly Dictionary<Collider, RacePlayerController>
        PlayersByCollider = new Dictionary<Collider, RacePlayerController>();

    private static readonly Dictionary<Rigidbody, RacePlayerController>
        PlayersByRigidbody = new Dictionary<Rigidbody, RacePlayerController>();

    public float Speed => CurrentSpeed;
    public bool IsGrounded => isGrounded;
    public bool IsStunned => Runner && !StunTimer.ExpiredOrNotRunning(Runner);

    private void Awake()
    {
        ValidateReferences();
        ConfigureRigidbody();
        DisableNetworkTransformForRace();
    }

    private void OnEnable()
    {
        RegisterCollisionReferences();
    }

    private void OnDisable()
    {
        UnregisterCollisionReferences();
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

        int ownerCharacterIndex = 0;

        if (playerCharacter)
            ownerCharacterIndex = playerCharacter.CharacterIndex;

        NetworkObject spawnedTrap = Runner.Spawn(
            trapPrefab,
            spawnPosition,
            spawnRotation,
            Object.InputAuthority,
            (spawnRunner, spawnedObject) =>
            {
                RaceTrap raceTrap =
                    NetworkObjectBehaviourReferences.GetRequired<RaceTrap>(
                        spawnedObject,
                        this
                    );

                if (raceTrap)
                    raceTrap.Initialize(Object.InputAuthority, ownerCharacterIndex);
            }
        );

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
        MoveWithCollision(movementDirection * (effectiveSpeed * deltaTime), settings);
    }

    private void MoveWithCollision(Vector3 displacement, RaceMovementSettings settings)
    {
        if (displacement.sqrMagnitude <= 0.000001f)
            return;

        Vector3 position = transform.position;
        Vector3 remaining = displacement;
        int iterations = Mathf.Max(1, settings.collisionSlideIterations);
        float skinWidth = Mathf.Max(0f, settings.collisionSkinWidth);

        for (int i = 0; i < iterations; i++)
        {
            float distance = remaining.magnitude;

            if (distance <= 0.0001f)
                break;

            Vector3 direction = remaining / distance;

            if (!TryCastBody(position, direction, distance + skinWidth, settings, out RaycastHit hit))
            {
                position += remaining;
                break;
            }

            float moveDistance = Mathf.Max(0f, hit.distance - skinWidth);
            position += direction * moveDistance;

            float leftoverDistance = Mathf.Max(0f, distance - moveDistance);
            remaining = Vector3.ProjectOnPlane(direction * leftoverDistance, hit.normal);
        }

        transform.position = position;
    }

    private bool TryCastBody(Vector3 position, Vector3 direction, float distance, RaceMovementSettings settings, out RaycastHit bestHit)
    {
        bestHit = default;

        if (!cachedCollider)
            return false;

        RaycastHit[] hits = cachedCollider is CapsuleCollider capsuleCollider
            ? CapsuleCastAll(position, direction, distance, capsuleCollider, settings.collisionMask)
            : SphereCastAll(position, direction, distance, settings.collisionMask);

        float closestDistance = float.MaxValue;
        bool foundHit = false;

        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider)
                continue;

            if (IsOwnCollider(hit.collider))
                continue;

            if (hit.collider == currentGroundCollider)
                continue;

            if (IsWalkableSurface(hit.normal, settings.walkableNormalThreshold))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            bestHit = hit;
            foundHit = true;
        }

        return foundHit;
    }

    private RaycastHit[] CapsuleCastAll(
        Vector3 position,
        Vector3 direction,
        float distance,
        CapsuleCollider capsuleCollider,
        LayerMask collisionMask
    )
    {
        Vector3 scale = transform.lossyScale;
        Vector3 centerOffset = transform.rotation * Vector3.Scale(capsuleCollider.center, scale);
        Vector3 center = position + centerOffset;
        Vector3 axis = GetCapsuleAxis(capsuleCollider.direction);
        float radiusScale = GetCapsuleRadiusScale(scale, capsuleCollider.direction);
        float heightScale = GetCapsuleHeightScale(scale, capsuleCollider.direction);
        float radius = Mathf.Max(0.001f, capsuleCollider.radius * radiusScale);
        float height = Mathf.Max(radius * 2f, capsuleCollider.height * heightScale);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 pointA = center + axis * halfSegment;
        Vector3 pointB = center - axis * halfSegment;

        return Physics.CapsuleCastAll(
            pointA,
            pointB,
            radius,
            direction,
            distance,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );
    }

    private RaycastHit[] SphereCastAll(Vector3 position, Vector3 direction, float distance, LayerMask collisionMask)
    {
        Bounds bounds = cachedCollider.bounds;
        Vector3 centerOffset = bounds.center - transform.position;
        Vector3 center = position + centerOffset;
        float radius = Mathf.Max(0.001f, Mathf.Max(bounds.extents.x, bounds.extents.z));

        return Physics.SphereCastAll(
            center,
            radius,
            direction,
            distance,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );
    }

    private Vector3 GetCapsuleAxis(int capsuleDirection)
    {
        switch (capsuleDirection)
        {
            case 0:
                return transform.right;
            case 2:
                return transform.forward;
            default:
                return transform.up;
        }
    }

    private float GetCapsuleHeightScale(Vector3 scale, int capsuleDirection)
    {
        switch (capsuleDirection)
        {
            case 0:
                return Mathf.Abs(scale.x);
            case 2:
                return Mathf.Abs(scale.z);
            default:
                return Mathf.Abs(scale.y);
        }
    }

    private float GetCapsuleRadiusScale(Vector3 scale, int capsuleDirection)
    {
        switch (capsuleDirection)
        {
            case 0:
                return Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            case 2:
                return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            default:
                return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
    }

    private bool IsWalkableSurface(Vector3 normal, float walkableNormalThreshold)
    {
        return Vector3.Dot(normal.normalized, Vector3.up) >= walkableNormalThreshold;
    }

    private void ConfigureRigidbody()
    {
        if (!cachedRigidbody)
            return;

        cachedRigidbody.isKinematic = true;
        cachedRigidbody.useGravity = false;
        cachedRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void DisableNetworkTransformForRace()
    {
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
            currentGroundCollider = hit.collider;
            verticalVelocity = 0f;

            float targetY = hit.point.y + GetGroundHeightOffset(settings);
            Vector3 position = transform.position;
            position.y = Mathf.MoveTowards(position.y, targetY, settings.groundSnapSpeed * deltaTime);
            transform.position = position;

            return;
        }

        isGrounded = false;
        groundNormal = Vector3.up;
        currentGroundCollider = null;
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
            return false;

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

    public static bool TryResolve(
        Collider sourceCollider,
        out RacePlayerController player)
    {
        player = null;

        if (sourceCollider == null)
            return false;

        if (PlayersByCollider.TryGetValue(sourceCollider, out player) && player)
            return true;

        Rigidbody attachedBody = sourceCollider.attachedRigidbody;

        return attachedBody != null &&
               PlayersByRigidbody.TryGetValue(attachedBody, out player) &&
               player != null;
    }

    private void RegisterCollisionReferences()
    {
        if (cachedRigidbody != null)
            PlayersByRigidbody[cachedRigidbody] = this;

        if (ownColliders == null)
            return;

        foreach (Collider ownCollider in ownColliders)
        {
            if (ownCollider != null)
                PlayersByCollider[ownCollider] = this;
        }
    }

    private void UnregisterCollisionReferences()
    {
        if (cachedRigidbody != null &&
            PlayersByRigidbody.TryGetValue(cachedRigidbody, out RacePlayerController bodyOwner) &&
            bodyOwner == this)
        {
            PlayersByRigidbody.Remove(cachedRigidbody);
        }

        if (ownColliders == null)
            return;

        foreach (Collider ownCollider in ownColliders)
        {
            if (ownCollider != null &&
                PlayersByCollider.TryGetValue(ownCollider, out RacePlayerController colliderOwner) &&
                colliderOwner == this)
            {
                PlayersByCollider.Remove(ownCollider);
            }
        }
    }

    private void ValidateReferences()
    {
        if (cachedRigidbody == null)
            Debug.LogError("RacePlayerController: Rigidbody is not assigned.", this);

        if (cachedCollider == null)
            Debug.LogError("RacePlayerController: Main Collider is not assigned.", this);

        if (ownColliders == null || ownColliders.Length == 0)
            Debug.LogError("RacePlayerController: Own Colliders are not assigned.", this);

        if (cachedNetworkTransform == null)
            Debug.LogError("RacePlayerController: Network Transform is not assigned.", this);

        if (playerCharacter == null)
            Debug.LogError("RacePlayerController: Player Character is not assigned.", this);
    }
    
    private void OnTrapHitCounterChanged()
    {
        RunRequiredCountingLoops();
        
        PlayTrapHitFlash();
        
        if (Object.HasInputAuthority)
            PlayLocalTrapHitFeedback();
    }
    
    private void PlayTrapHitFlash()
    {
        if (hitFlashRenderers == null || hitFlashRenderers.Length == 0)
            return;

        if (hitFlashRoutine != null)
            StopCoroutine(hitFlashRoutine);

        hitFlashRoutine = StartCoroutine(FlashAfterTrapHit());
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
