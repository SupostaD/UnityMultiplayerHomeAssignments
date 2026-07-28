using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class HexBallPlayerController :
    NetworkBehaviour,
    INetworkRunnerCallbacks,
    IStateAuthorityChanged
{
    [Header("Physics")]
    [SerializeField] private Rigidbody body;
    [SerializeField] private Collider bodyCollider;
    [SerializeField] private NetworkPlayerCharacter playerCharacter;
    [SerializeField] private NetworkTransform networkTransform;
    [SerializeField] private HexPlayerAbilityController abilityController;
    [SerializeField] private bool configurePhysicsByAuthority = true;
    [SerializeField] private bool stateAuthorityUsesGravity = true;
    [SerializeField, Range(0f, 0.5f)] private float inputDeadZone = 0.08f;

    [Header("Jump")]
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField, Min(0.1f)] private float jumpSpeed = 7f;
    [SerializeField, Min(0.01f)] private float groundCheckDistance = 0.2f;
    [SerializeField, Range(0.5f, 0.99f)] private float groundCheckRadiusScale = 0.9f;
    [SerializeField, Range(0f, 1f)] private float minimumGroundNormalY = 0.55f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private BallJumpVFX jumpVFX;
    
    [Header("Dash")]
    [SerializeField] private InputActionReference dashAction;
    [SerializeField, Min(0.1f)] private float dashSpeed = 16f;
    [SerializeField, Min(0.05f)] private float dashCooldownSeconds = 1f;
    [SerializeField, Min(0.05f)] private float dashBoostDuration = 0.2f;
    [SerializeField] private BallDashVFX dashVFX;

    [Header("Hex Capture")]
    [SerializeField, Min(0.05f)] private float captureRetrySeconds = 0.15f;

    [Header("New Input System")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private bool createDefaultMoveAction = true;

    [Header("Camera Relative Movement")]
    [SerializeField] private NetworkThirdPersonCameraRig cameraRig;
    
    [Header("Audio")]
    [SerializeField] private bool playSounds = true;

    public bool IsGroundedForVFX()
    {
        return IsGrounded();
    }
    
    private float GetActiveMaximumSpeed()
    {
        if (movementBoostTimer.ExpiredOrNotRunning(Runner))
            return currentMaximumSpeed;

        return currentMaximumSpeed *
               Mathf.Max(1f, movementBoostMultiplier);
    }
    
    public float Strength
    {
        get
        {
            HexTerritoryManager territoryManager = GetTerritoryManager();

            return territoryManager != null && Object != null
                ? territoryManager.GetStrength(Object.InputAuthority)
                : 0f;
        }
    }

    public int DisplayedStrength
    {
        get
        {
            HexTerritoryManager territoryManager = GetTerritoryManager();

            return territoryManager != null && Object != null
                ? territoryManager.GetDisplayedStrength(Object.InputAuthority)
                : 0;
        }
    }

    public bool IsEliminated
    {
        get
        {
            HexTerritoryManager territoryManager = GetTerritoryManager();

            return territoryManager != null &&
                   Object != null &&
                   territoryManager.IsPlayerEliminated(Object.InputAuthority);
        }
    }

    public int TerritoryCount
    {
        get
        {
            HexTerritoryManager territoryManager = GetTerritoryManager();

            return territoryManager != null && Object != null
                ? territoryManager.GetTerritoryCount(Object.InputAuthority)
                : 0;
        }
    }

    public float CurrentMass
    {
        get
        {
            HexPlayerGrowthSettings settings = GetGrowthSettings();

            return settings != null
                ? settings.EvaluateMass(TerritoryCount)
                : body != null
                    ? body.mass
                    : 1f;
        }
    }

    public float CollisionRadius
    {
        get
        {
            if (bodyCollider == null)
                return 0f;

            Bounds bounds = bodyCollider.bounds;
            return Mathf.Max(
                bounds.extents.x,
                Mathf.Max(bounds.extents.y, bounds.extents.z)
            );
        }
    }

    private NetworkRunner registeredRunner;
    private InputAction fallbackMoveAction;
    private bool localInputEnabled;
    private RigidbodyConstraints initialConstraints;
    private PhysicsMaterial initialColliderMaterial;
    private PhysicsMaterial slidingMaterial;
    private readonly RaycastHit[] groundHits = new RaycastHit[8];
    private NetworkButtons previousButtons;
    private TickTimer captureRetryTimer;
    private HexCoord lastCaptureCoordinate;
    private bool hasLastCaptureCoordinate;
    private HexCoord lastSafeCoordinate;
    private bool hasLastSafeCoordinate;
    private bool missingTerritoryManagerLogged;
    private bool eliminationStateApplied;
    private bool appliedEliminatedState;
    private float currentMaximumSpeed = 9f;
    private float currentAcceleration = 28f;
    private float currentBraking = 34f;
    private int jumpsUsed;
    private TickTimer movementBoostTimer;
    private TickTimer dashCooldownTimer;
    private TickTimer impactRecoveryTimer;
    private float movementBoostMultiplier = 1f;
    private float impactSpeedAllowance;
    private float impactSpeedAllowanceDecay;
    private int nextImpactSequence;
    private Vector3 pendingImpactVelocityCorrection;
    private float impactCorrectionTimeRemaining;
    private readonly Dictionary<int, TickTimer> contactReportCooldowns =
        new Dictionary<int, TickTimer>();
    private sealed class LocalPredictedImpact
    {
        public PlayerRef OtherPlayer;
        public Vector3 ResolvedPlanarVelocity;
        public float VerticalLift;
        public float ExpiryTime;
    }

    private readonly Dictionary<int, LocalPredictedImpact>
        predictedImpacts =
            new Dictionary<int, LocalPredictedImpact>();
    private readonly Dictionary<int, float>
        localImpactCooldownExpiryTimes =
            new Dictionary<int, float>();
    private readonly List<int> predictedImpactKeysToRemove =
        new List<int>();

    private static readonly Dictionary<Collider, HexBallPlayerController>
        PlayersByCollider =
            new Dictionary<Collider, HexBallPlayerController>();

    private static readonly Dictionary<Rigidbody, HexBallPlayerController>
        PlayersByRigidbody =
            new Dictionary<Rigidbody, HexBallPlayerController>();

    private static readonly HashSet<HexBallPlayerController>
        ActivePlayers =
            new HashSet<HexBallPlayerController>();
    
    public bool HasDashAbility =>
        abilityController != null &&
        abilityController.HasAbility(HexPlayerAbilityType.Dash);
    
    [Networked]
    private Vector3 LastDashDirection { get; set; }

    [Networked]
    private Vector3 SimulatedVelocity { get; set; }

    [Networked]
    private float VelocitySampleSimulationTime { get; set; }

    [Networked]
    private TickTimer DashImpactTimer { get; set; }

    public Vector3 NetworkVelocity => SimulatedVelocity;

    public Vector3 CollisionVelocity => SimulatedVelocity;

    public bool IsDashImpactActive =>
        Runner != null &&
        !DashImpactTimer.ExpiredOrNotRunning(Runner);
    
    [Networked, OnChangedRender(nameof(OnDashVersionChanged))]
    private int DashVersion { get; set; }
    
    [Networked, OnChangedRender(nameof(OnDoubleJumpVersionChanged))]
    private int DoubleJumpVersion { get; set; }
    
    [Networked, OnChangedRender(nameof(OnJumpVersionChanged))]
    private int JumpVersion { get; set; }
    
    [Networked, OnChangedRender(nameof(OnTrampolineVersionChanged))]
    private int TrampolineVersion { get; set; }

    private void Awake()
    {
        ValidateReferences();

        if (body != null)
            initialConstraints = body.constraints;

        if (bodyCollider != null)
            initialColliderMaterial = bodyCollider.sharedMaterial;

        if (createDefaultMoveAction)
            CreateFallbackMoveAction();
    }

    public override void Spawned()
    {
        RegisterCollisionReferences();

        if (body == null)
        {
            Debug.LogError("HexBallPlayerController requires a Rigidbody reference.", this);
            return;
        }

        ValidateSettings();
        
        RefreshGrowthState();
        ConfigurePhysicsForAuthority();
        RefreshEliminationState();

        if (!configurePhysicsByAuthority && body.isKinematic)
        {
            Debug.LogWarning(
                "The ball Rigidbody is kinematic. Disable Is Kinematic for physical movement.",
                this
            );
        }

        if (Object.HasInputAuthority)
            RegisterLocalInput();
    }

    public void StateAuthorityChanged()
    {
        RefreshGrowthState();
        ConfigurePhysicsForAuthority();
        RefreshEliminationState();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        UnregisterCollisionReferences();
        UnregisterLocalInput();
    }

    private void OnEnable()
    {
        RegisterCollisionReferences();
    }

    private void OnDisable()
    {
        UnregisterCollisionReferences();

        if (localInputEnabled)
            DisableInputActions();
    }

    private void OnDestroy()
    {
        UnregisterLocalInput();
        RestoreColliderMaterial();

        if (slidingMaterial != null)
        {
            Destroy(slidingMaterial);
            slidingMaterial = null;
        }

        fallbackMoveAction?.Dispose();
        fallbackMoveAction = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (body == null)
            return;

        RefreshGrowthState();
        RefreshEliminationState();

        if (!Object.HasStateAuthority)
            return;

        if (IsEliminated)
        {
            StopBodyMotion();
            SimulatedVelocity = Vector3.zero;
            VelocitySampleSimulationTime =
                Runner.SimulationTime;
            return;
        }

        HexBallInputData input = default;

        if (!GetInput(out input))
            input = default;

        GameSceneManager sceneManager =
            GameSceneManager.Instance;
        bool waitingForMatchStart =
            sceneManager != null &&
            !sceneManager.IsMatchStarted;

        if (GameInputBlocker.IsGameplayInputBlocked ||
            waitingForMatchStart ||
            (sceneManager != null && sceneManager.IsGameEnded))
        {
            input = default;
        }

        ConfigureRollingMode(true);

        if (ShouldDisablePhysicalRolling())
            body.angularVelocity = Vector3.zero;

        bool grounded = IsGrounded();

        if (grounded && body.linearVelocity.y <= 0.1f)
            jumpsUsed = 0;

        bool jumpPressed = input.Buttons.WasPressed(
            previousButtons,
            HexBallInputButton.Jump
        );

        bool dashPressed = input.Buttons.WasPressed(
            previousButtons,
            HexBallInputButton.Dash
            );

        previousButtons = input.Buttons;

        if (jumpPressed)
            TryJump(grounded);

        if (dashPressed)
            TryDash(input.Move);

        Move(input.Move, Runner.DeltaTime);
        ApplyPendingImpactCorrection(Runner.DeltaTime);
        TryPredictUpcomingPlayerImpact();
        UpdatePhysicalRolling();
        SimulatedVelocity = body.linearVelocity;
        VelocitySampleSimulationTime =
            Runner.SimulationTime;
        CleanupExpiredPredictedImpacts();

        if (!waitingForMatchStart)
            TryCaptureCurrentHex();
    }

    public override void Render()
    {
        RefreshGrowthState();
        RefreshEliminationState();
    }

    public void InitializeSpawnTransform(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        ResetImpactPredictionState();

        if (body != null)
        {
            body.position = spawnPosition;
            body.rotation = spawnRotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        else
        {
            transform.SetPositionAndRotation(
                spawnPosition,
                spawnRotation
            );
        }

        RememberSafeHexAtPosition(spawnPosition);
    }

    public bool ReturnToLastSafeHex()
    {
        if (Object == null ||
            !Object.HasStateAuthority ||
            !hasLastSafeCoordinate)
        {
            return false;
        }

        HexTerritoryManager territoryManager =
            GetTerritoryManager();

        if (territoryManager == null ||
            !territoryManager.TryGetTileTopCenter(
                lastSafeCoordinate,
                out Vector3 tileTopCenter))
        {
            return false;
        }

        HexGameRulesSettings rules =
            GetGameRulesSettings();
        float clearance = rules != null
            ? rules.DeathZoneReturnClearance
            : 0.1f;
        Vector3 returnPosition =
            tileTopCenter +
            Vector3.up *
            (Mathf.Max(0.01f, CollisionRadius) + clearance);
        Quaternion returnRotation =
            body != null ? body.rotation : transform.rotation;

        InitializeSpawnTransform(
            returnPosition,
            returnRotation
        );

        if (networkTransform != null)
        {
            networkTransform.Teleport(
                returnPosition,
                returnRotation
            );
        }
        else
        {
            Debug.LogError(
                "HexBallPlayerController: Network Transform is not assigned.",
                this
            );
        }

        hasLastCaptureCoordinate = false;
        captureRetryTimer = default;
        return true;
    }

    private void ValidateReferences()
    {
        if (body == null)
            Debug.LogError(
                "HexBallPlayerController: Body Rigidbody is not assigned.",
                this
            );

        if (bodyCollider == null)
            Debug.LogError(
                "HexBallPlayerController: Body Collider is not assigned.",
                this
            );

        if (playerCharacter == null)
            Debug.LogError(
                "HexBallPlayerController: Player Character is not assigned.",
                this
            );

        if (networkTransform == null)
            Debug.LogError(
                "HexBallPlayerController: Network Transform is not assigned.",
                this
            );

        if (abilityController == null)
            Debug.LogError(
                "HexBallPlayerController: Ability Controller is not assigned.",
                this
            );

        if (cameraRig == null)
            Debug.LogError(
                "HexBallPlayerController: Camera Rig is not assigned.",
                this
            );

        if (jumpAction == null || jumpAction.action == null)
            Debug.LogError(
                "HexBallPlayerController: Jump Action is not assigned.",
                this
            );
    }

    private void ValidateSettings()
    {
        if (GetGrowthSettings() == null)
        {
            Debug.LogError(
                "HexBallPlayerController: Player Growth Settings is not assigned on GameSceneManager.",
                this
            );
        }

        if (GetGameRulesSettings() == null)
        {
            Debug.LogError(
                "HexBallPlayerController: Game Rules Settings is not assigned on GameSceneManager.",
                this
            );
        }
    }

    private void Move(Vector2 moveInput, float deltaTime)
    {
        UpdateImpactRecovery(deltaTime);

        float deadZone = Mathf.Clamp(inputDeadZone, 0f, 0.5f);

        if (moveInput.sqrMagnitude < deadZone * deadZone)
            moveInput = Vector2.zero;
        else if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        Vector3 currentVelocity = body.linearVelocity;
        Vector3 currentPlanarVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
        Vector3 desiredDirection = new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 desiredVelocity =
            desiredDirection * Mathf.Max(0.1f, GetActiveMaximumSpeed());
        float response = desiredDirection.sqrMagnitude > 0f
            ? Mathf.Max(0.1f, currentAcceleration)
            : Mathf.Max(0.1f, currentBraking);
        response *= GetImpactControlMultiplier();

        float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
        Vector3 requestedAcceleration =
            (desiredVelocity - currentPlanarVelocity) / safeDeltaTime;

        body.AddForce(
            Vector3.ClampMagnitude(requestedAcceleration, response),
            ForceMode.Acceleration
        );

        ClampPlanarSpeed(
            currentVelocity.y,
            Mathf.Max(0.1f, GetActiveMaximumSpeed()) +
            impactSpeedAllowance
        );
    }

    private void TryJump(bool grounded)
    {
        if (grounded && body.linearVelocity.y <= 0.1f)
            jumpsUsed = 0;
        
        if (!grounded && jumpsUsed == 0)
            jumpsUsed = 1;

        bool isDoubleJump = jumpsUsed >= 1;

        if (jumpsUsed >= 2)
            return;

        if (isDoubleJump &&
            (abilityController == null ||
             !abilityController.TryConsumeAbility(
                 HexPlayerAbilityType.DoubleJump)))
        {
            return;
        }

        jumpsUsed++;

        Vector3 velocity = body.linearVelocity;
        velocity.y = Mathf.Max(0.1f, jumpSpeed);
        body.linearVelocity = velocity;

        if (isDoubleJump)
            DoubleJumpVersion++;
        else
            JumpVersion++;
    }

    private void TryDash(Vector2 moveInput)
    {
        if (!dashCooldownTimer.ExpiredOrNotRunning(Runner))
            return;
        
        Vector3 dashDirection = GetDashDirection(moveInput);
        
        if (dashDirection.sqrMagnitude <= 0.001f)
            return;

        if (abilityController == null ||
            !abilityController.TryConsumeAbility(
                HexPlayerAbilityType.Dash))
        {
            return;
        }
        
        Vector3 currentVelocity = body.linearVelocity;
        Vector3 dashVelocity = dashDirection.normalized * dashSpeed;
        
        movementBoostMultiplier = Mathf.Max(1f, 
            dashSpeed / Mathf.Max(0.1f, currentMaximumSpeed)
            );

        movementBoostTimer = TickTimer.CreateFromSeconds(
            Runner,
            Mathf.Max(0.05f, dashBoostDuration)
            );

        body.linearVelocity = new Vector3(
            dashVelocity.x,
            currentVelocity.y,
            dashVelocity.z);

        dashCooldownTimer = TickTimer.CreateFromSeconds(
            Runner,
            Mathf.Max(0.05f, dashCooldownSeconds)
        );
        DashImpactTimer = TickTimer.CreateFromSeconds(
            Runner,
            Mathf.Max(0.05f, dashBoostDuration)
        );
        
        LastDashDirection = dashDirection.normalized;
        DashVersion++;
    }

    private Vector3 GetDashDirection(Vector2 moveInput)
    {
        Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y);
        
        if (inputDirection.sqrMagnitude > 0.01f)
            return inputDirection.normalized;
        
        Vector3 planarVelocity = body.linearVelocity;
        planarVelocity.y = 0f;
        
        if (planarVelocity.sqrMagnitude > 0.01f)
            return planarVelocity.normalized;
        
        return transform.forward;
    }

    private void OnDoubleJumpVersionChanged()
    {
        jumpVFX?.PlayDoubleJump();
        if (playSounds)
            GameSoundManager.Instance?.PlayDoubleJump(transform.position);
    }
    
    private void OnJumpVersionChanged()
    {
        if (playSounds)
            GameSoundManager.Instance?.PlayJump(transform.position);
    }

    private void OnDashVersionChanged()
    {
        Vector3 dashDirection = LastDashDirection;
        
        if (dashDirection.sqrMagnitude <= 0.001f)
            dashDirection = transform.forward;
        
        dashVFX?.PlayDash(dashDirection);
        if (playSounds)
            GameSoundManager.Instance?.PlayDash(transform.position);
    }
    
    private void OnTrampolineVersionChanged()
    {
        if (playSounds)
            GameSoundManager.Instance?.PlayTrampoline(transform.position);
    }

    public void GiveDashAbility()
    {
        TryGrantAbility(HexPlayerAbilityType.Dash);
    }

    public bool TryGrantAbility(
        HexPlayerAbilityType abilityType)
    {
        return abilityController != null &&
               abilityController.TryGrantAbility(abilityType);
    }
    
    public void ApplyTrampolineLaunch(Vector3 fallbackDirection, float verticalSpeed,
        float forwardVelocityChange, float speedMultiplier, float boostDuration)
    {
        if (!body || !Object || !Object.HasStateAuthority || IsEliminated) 
            return;

        Vector3 currentVelocity = body.linearVelocity;

        Vector3 planarVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

        Vector3 launchDirection;

        if (planarVelocity.sqrMagnitude > 0.25f) 
            launchDirection = planarVelocity.normalized;
        else
        {
            fallbackDirection.y = 0f;

            launchDirection = fallbackDirection.sqrMagnitude > 0.001f
                    ? fallbackDirection.normalized : transform.forward;
        }

        movementBoostMultiplier = Mathf.Max(1f, speedMultiplier);

        movementBoostTimer = TickTimer.CreateFromSeconds(
            Runner,Mathf.Max(0.05f, boostDuration));

        Vector3 boostedPlanarVelocity = planarVelocity + launchDirection * 
            Mathf.Max(0f, forwardVelocityChange);

        boostedPlanarVelocity = Vector3.ClampMagnitude(
                boostedPlanarVelocity, GetActiveMaximumSpeed());

        body.linearVelocity = new Vector3(
            boostedPlanarVelocity.x, Mathf.Max(0.1f, verticalSpeed), boostedPlanarVelocity.z);
        
        jumpsUsed = 1;
        
        TrampolineVersion++;
    }

    private bool IsGrounded()
    {
        if (bodyCollider == null)
            return false;

        Bounds bounds = bodyCollider.bounds;
        float radius = Mathf.Max(
            0.01f,
            Mathf.Min(bounds.extents.x, bounds.extents.z) *
            Mathf.Clamp(groundCheckRadiusScale, 0.5f, 0.99f)
        );
        float centerToCastBottom = Mathf.Max(0f, bounds.extents.y - radius);
        float castDistance =
            centerToCastBottom + Mathf.Max(0.01f, groundCheckDistance);
        int hitCount = Physics.SphereCastNonAlloc(
            bounds.center,
            radius,
            Vector3.down,
            groundHits,
            castDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];

            if (hit.collider == null || hit.collider == bodyCollider)
                continue;

            if (hit.normal.y >= minimumGroundNormalY)
                return true;
        }

        return false;
    }

    public bool IsTouchingGroundForCapture()
    {
        return body != null &&
               bodyCollider != null &&
               bodyCollider.enabled &&
               IsGrounded();
    }

    private void TryCaptureCurrentHex()
    {
        GameSceneManager sceneManager = GameSceneManager.Instance;
        HexTerritoryManager territoryManager =
            sceneManager != null ? sceneManager.HexTerritory : null;

        if (territoryManager == null)
        {
            if (!missingTerritoryManagerLogged)
            {
                Debug.LogError(
                    "HexBallPlayerController: Hex Territory Manager is not assigned on GameSceneManager.",
                    this
                );
                missingTerritoryManagerLogged = true;
            }

            return;
        }

        missingTerritoryManagerLogged = false;

        if (!IsTouchingGroundForCapture())
        {
            hasLastCaptureCoordinate = false;
            return;
        }

        if (!territoryManager.TryGetCoordinate(
                body.worldCenterOfMass,
                out HexCoord coordinate))
        {
            hasLastCaptureCoordinate = false;
            return;
        }

        lastSafeCoordinate = coordinate;
        hasLastSafeCoordinate = true;

        if (territoryManager.IsOwnedBy(coordinate, Object.InputAuthority))
        {
            lastCaptureCoordinate = coordinate;
            hasLastCaptureCoordinate = true;
            return;
        }

        bool changedCoordinate =
            !hasLastCaptureCoordinate ||
            lastCaptureCoordinate != coordinate;

        if (!changedCoordinate &&
            !captureRetryTimer.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        lastCaptureCoordinate = coordinate;
        hasLastCaptureCoordinate = true;
        captureRetryTimer = TickTimer.CreateFromSeconds(
            Runner,
            Mathf.Max(0.05f, captureRetrySeconds)
        );
        territoryManager.RequestCapture(
            coordinate,
            body.position,
            true
        );
    }

    private void RememberSafeHexAtPosition(
        Vector3 worldPosition)
    {
        HexTerritoryManager territoryManager =
            GetTerritoryManager();

        if (territoryManager == null ||
            !territoryManager.TryGetCoordinate(
                worldPosition,
                out HexCoord coordinate))
        {
            return;
        }

        lastSafeCoordinate = coordinate;
        hasLastSafeCoordinate = true;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (Object == null ||
            !Object.HasStateAuthority ||
            (GameSceneManager.Instance != null &&
             !GameSceneManager.Instance.IsMatchStarted) ||
            IsEliminated ||
            collision == null ||
            !TryResolvePlayer(collision.collider, out HexBallPlayerController otherPlayer) ||
            otherPlayer == this ||
            otherPlayer.Object == null ||
            otherPlayer.IsEliminated)
        {
            return;
        }

        PlayerRef otherPlayerRef = otherPlayer.Object.InputAuthority;
        int cooldownKey = otherPlayerRef.PlayerId;
        HexStrengthCombatManager combatManager = GetCombatManager();

        if (combatManager == null)
            return;

        if (contactReportCooldowns.TryGetValue(
                cooldownKey,
                out TickTimer cooldown) &&
            !cooldown.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        contactReportCooldowns[cooldownKey] = TickTimer.CreateFromSeconds(
            Runner,
            combatManager.ContactReportIntervalSeconds
        );
        combatManager.RequestContact(otherPlayerRef);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!CanInteractWithPlayerCollision(
                collision,
                out HexBallPlayerController otherPlayer))
        {
            return;
        }

        HexStrengthCombatManager combatManager = GetCombatManager();

        if (combatManager != null)
        {
            PlayerRef otherPlayerRef =
                otherPlayer.Object.InputAuthority;

            combatManager.RequestContact(otherPlayerRef);

            if (IsLocalImpactOnCooldown(otherPlayerRef))
                return;

            Vector3 directionToOther =
                otherPlayer.transform.position -
                transform.position;
            directionToOther.y = 0f;
            Vector3 reportedVelocity = CollisionVelocity;
            bool isDashing = IsDashImpactActive;
            int impactSequence = CreateImpactSequence();

            TryApplyPredictedImpact(
                otherPlayer,
                directionToOther,
                reportedVelocity,
                isDashing,
                impactSequence
            );

            combatManager.RequestImpact(
                otherPlayerRef,
                body.position,
                reportedVelocity,
                directionToOther,
                isDashing,
                impactSequence
            );
        }
    }

    private void TryPredictUpcomingPlayerImpact()
    {
        HexPlayerCollisionSettings settings =
            GetCollisionSettings();

        if (settings == null ||
            !settings.EnableEarlyCollisionPrediction ||
            body == null ||
            bodyCollider == null ||
            !bodyCollider.enabled ||
            Object == null ||
            !Object.HasStateAuthority ||
            Runner == null ||
            IsEliminated)
        {
            return;
        }

        GameSceneManager sceneManager =
            GameSceneManager.Instance;

        if (sceneManager != null &&
            (!sceneManager.IsMatchStarted ||
             sceneManager.IsGameEnded))
        {
            return;
        }

        HexBallPlayerController earliestPlayer = null;
        Vector3 earliestDirection = Vector3.zero;
        float earliestImpactTime = float.PositiveInfinity;

        foreach (HexBallPlayerController otherPlayer in ActivePlayers)
        {
            if (!CanPredictImpactWith(otherPlayer) ||
                IsLocalImpactOnCooldown(
                    otherPlayer.Object.InputAuthority) ||
                !TryCalculatePredictedImpact(
                    otherPlayer,
                    settings,
                    out float impactTime,
                    out Vector3 directionToOther) ||
                impactTime >= earliestImpactTime)
            {
                continue;
            }

            earliestPlayer = otherPlayer;
            earliestDirection = directionToOther;
            earliestImpactTime = impactTime;
        }

        if (earliestPlayer == null)
            return;

        TrySubmitEarlyPredictedImpact(
            earliestPlayer,
            earliestDirection
        );
    }

    private bool CanPredictImpactWith(
        HexBallPlayerController otherPlayer)
    {
        return otherPlayer != null &&
               otherPlayer != this &&
               otherPlayer.isActiveAndEnabled &&
               otherPlayer.body != null &&
               otherPlayer.bodyCollider != null &&
               otherPlayer.bodyCollider.enabled &&
               otherPlayer.Object != null &&
               otherPlayer.Runner == Runner &&
               otherPlayer.Object.InputAuthority != PlayerRef.None &&
               Object.InputAuthority !=
               otherPlayer.Object.InputAuthority &&
               !otherPlayer.IsEliminated;
    }

    private bool TryCalculatePredictedImpact(
        HexBallPlayerController otherPlayer,
        HexPlayerCollisionSettings settings,
        out float impactTime,
        out Vector3 directionToOther)
    {
        impactTime = 0f;
        directionToOther = Vector3.zero;

        float ownRadius = CollisionRadius;
        float otherRadius = otherPlayer.CollisionRadius;

        if (ownRadius <= 0f || otherRadius <= 0f)
            return false;

        Vector3 ownVelocity = body.linearVelocity;
        Vector3 otherVelocity = otherPlayer.CollisionVelocity;
        float extrapolationSeconds =
            GetRemoteExtrapolationSeconds(
                settings,
                otherPlayer
            );
        Vector3 predictedOtherPosition =
            otherPlayer.body.position +
            otherVelocity * extrapolationSeconds;
        Vector3 relativePosition =
            predictedOtherPosition - body.position;
        Vector3 relativeVelocity =
            otherVelocity - ownVelocity;
        float combinedRadius =
            ownRadius +
            otherRadius +
            settings.PredictionContactPadding;
        float radiusSquared =
            combinedRadius * combinedRadius;
        float relativeSpeedSquared =
            relativeVelocity.sqrMagnitude;

        if (relativeSpeedSquared <= 0.0001f)
            return false;

        float positionVelocityDot =
            Vector3.Dot(relativePosition, relativeVelocity);
        float minimumPredictiveClosingSpeed =
            Mathf.Max(0.05f, settings.MinimumImpactSpeed);
        float separationDistance =
            Mathf.Max(0.0001f, relativePosition.magnitude);
        float closingSpeed =
            -positionVelocityDot / separationDistance;

        if (closingSpeed < minimumPredictiveClosingSpeed)
            return false;

        float distanceFromContact =
            relativePosition.sqrMagnitude - radiusSquared;

        if (distanceFromContact <= 0f)
        {
            impactTime = 0f;
        }
        else
        {
            float quadraticB = 2f * positionVelocityDot;
            float discriminant =
                quadraticB * quadraticB -
                4f *
                relativeSpeedSquared *
                distanceFromContact;

            if (discriminant < 0f)
                return false;

            impactTime =
                (-quadraticB - Mathf.Sqrt(discriminant)) /
                (2f * relativeSpeedSquared);

            if (impactTime < 0f ||
                impactTime > settings.CollisionLookAheadSeconds)
            {
                return false;
            }
        }

        Vector3 predictedSeparation =
            relativePosition +
            relativeVelocity * impactTime;
        predictedSeparation.y = 0f;

        if (predictedSeparation.sqrMagnitude <= 0.0001f)
        {
            predictedSeparation =
                otherPlayer.transform.position -
                transform.position;
            predictedSeparation.y = 0f;
        }

        if (predictedSeparation.sqrMagnitude <= 0.0001f)
            return false;

        directionToOther = predictedSeparation.normalized;
        return true;
    }

    private float GetRemoteExtrapolationSeconds(
        HexPlayerCollisionSettings settings,
        HexBallPlayerController otherPlayer)
    {
        if (settings == null)
            return 0f;

        float extrapolationSeconds =
            settings.MinimumRemoteExtrapolationSeconds;
        bool usedNetworkStateAge = false;

        if (settings.UseNetworkStateAgeForExtrapolation &&
            Runner != null &&
            otherPlayer != null &&
            otherPlayer.VelocitySampleSimulationTime > 0f)
        {
            float stateAgeSeconds =
                Runner.SimulationTime -
                otherPlayer.VelocitySampleSimulationTime;

            if (stateAgeSeconds >= 0f)
            {
                extrapolationSeconds =
                    stateAgeSeconds *
                    settings.NetworkStateAgeMultiplier;
                usedNetworkStateAge = true;
            }
        }

        if (!usedNetworkStateAge &&
            settings.UseRttForRemoteExtrapolation &&
            Runner != null)
        {
            double measuredRtt =
                Runner.GetPlayerRtt(PlayerRef.None);

            if (!double.IsNaN(measuredRtt) &&
                !double.IsInfinity(measuredRtt) &&
                measuredRtt > 0.0)
            {
                extrapolationSeconds =
                    (float)measuredRtt *
                    settings.RttExtrapolationMultiplier;
            }
        }

        return Mathf.Clamp(
            extrapolationSeconds,
            settings.MinimumRemoteExtrapolationSeconds,
            settings.MaximumRemoteExtrapolationSeconds
        );
    }

    private bool TrySubmitEarlyPredictedImpact(
        HexBallPlayerController otherPlayer,
        Vector3 directionToOther)
    {
        HexStrengthCombatManager combatManager =
            GetCombatManager();

        if (combatManager == null ||
            otherPlayer == null ||
            otherPlayer.Object == null)
        {
            return false;
        }

        PlayerRef otherPlayerRef =
            otherPlayer.Object.InputAuthority;
        Vector3 reportedVelocity = body.linearVelocity;
        bool isDashing = IsDashImpactActive;
        int impactSequence = CreateImpactSequence();

        if (!TryApplyPredictedImpact(
                otherPlayer,
                directionToOther,
                reportedVelocity,
                isDashing,
                impactSequence))
        {
            return false;
        }

        combatManager.RequestImpact(
            otherPlayerRef,
            body.position,
            reportedVelocity,
            directionToOther,
            isDashing,
            impactSequence
        );
        return true;
    }

    private bool IsLocalImpactOnCooldown(
        PlayerRef otherPlayer)
    {
        return otherPlayer != PlayerRef.None &&
               localImpactCooldownExpiryTimes.TryGetValue(
                   otherPlayer.PlayerId,
                   out float cooldownExpiryTime) &&
               Time.unscaledTime < cooldownExpiryTime;
    }

    private bool CanInteractWithPlayerCollision(
        Collision collision,
        out HexBallPlayerController otherPlayer)
    {
        otherPlayer = null;

        return Object != null &&
               Object.HasStateAuthority &&
               (GameSceneManager.Instance == null ||
                GameSceneManager.Instance.IsMatchStarted) &&
               !IsEliminated &&
               collision != null &&
               TryResolvePlayer(collision.collider, out otherPlayer) &&
               otherPlayer != this &&
               otherPlayer.Object != null &&
               !otherPlayer.IsEliminated;
    }

    public static bool TryResolvePlayer(
        Collider sourceCollider,
        out HexBallPlayerController player)
    {
        player = null;

        if (sourceCollider == null)
            return false;

        if (PlayersByCollider.TryGetValue(sourceCollider, out player) &&
            player != null)
        {
            return true;
        }

        Rigidbody attachedBody = sourceCollider.attachedRigidbody;

        return attachedBody != null &&
               PlayersByRigidbody.TryGetValue(attachedBody, out player) &&
               player != null;
    }

    public void RequestResolvedImpactVelocity(
        PlayerRef otherPlayer,
        int predictionSequence,
        Vector3 resolvedPlanarVelocity,
        float verticalLift,
        bool isRejection)
    {
        if (Object == null || IsEliminated)
            return;

        RPC_ApplyResolvedImpactVelocity(
            otherPlayer,
            predictionSequence,
            resolvedPlanarVelocity,
            verticalLift,
            isRejection
        );
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ApplyResolvedImpactVelocity(
        PlayerRef otherPlayer,
        int predictionSequence,
        Vector3 resolvedPlanarVelocity,
        float verticalLift,
        bool isRejection)
    {
        if (body == null ||
            Object == null ||
            !Object.HasStateAuthority ||
            IsEliminated)
        {
            return;
        }

        HexPlayerCollisionSettings settings = GetCollisionSettings();

        if (settings == null)
            return;

        if (predictionSequence > 0 &&
            predictedImpacts.TryGetValue(
                predictionSequence,
                out LocalPredictedImpact predictedImpact) &&
            predictedImpact.OtherPlayer == otherPlayer &&
            Time.unscaledTime <= predictedImpact.ExpiryTime)
        {
            Vector3 correction = new Vector3(
                resolvedPlanarVelocity.x -
                predictedImpact.ResolvedPlanarVelocity.x,
                Mathf.Max(0f, verticalLift) -
                predictedImpact.VerticalLift,
                resolvedPlanarVelocity.z -
                predictedImpact.ResolvedPlanarVelocity.z
            );
            correction = Vector3.ClampMagnitude(
                correction,
                settings.MaximumReconciliationVelocityChange
            );
            QueueImpactCorrection(
                correction,
                settings.ReconciliationSeconds
            );
            predictedImpacts.Remove(predictionSequence);
            StartImpactRecovery(body.linearVelocity + correction);
            return;
        }

        if (isRejection)
            return;

        RemovePredictedImpactsForOther(otherPlayer);

        Vector3 currentVelocity = body.linearVelocity;
        Vector3 resolvedVelocity = new Vector3(
            resolvedPlanarVelocity.x,
            currentVelocity.y + Mathf.Max(0f, verticalLift),
            resolvedPlanarVelocity.z
        );
        body.linearVelocity = resolvedVelocity;
        SimulatedVelocity = resolvedVelocity;
        StartImpactRecovery(resolvedVelocity);
    }

    private void StartImpactRecovery(Vector3 resolvedVelocity)
    {
        HexPlayerCollisionSettings settings = GetCollisionSettings();

        if (settings == null)
            return;

        float activeMaximumSpeed =
            Mathf.Max(0.1f, GetActiveMaximumSpeed());
        Vector3 appliedPlanarVelocity =
            new Vector3(resolvedVelocity.x, 0f, resolvedVelocity.z);
        impactSpeedAllowance = Mathf.Max(
            impactSpeedAllowance,
            Mathf.Max(
                0f,
                appliedPlanarVelocity.magnitude - activeMaximumSpeed
            )
        );
        impactSpeedAllowanceDecay =
            impactSpeedAllowance /
            settings.ControlRecoverySeconds;
        impactRecoveryTimer = TickTimer.CreateFromSeconds(
            Runner,
            settings.ControlRecoverySeconds
        );
    }

    private bool TryApplyPredictedImpact(
        HexBallPlayerController otherPlayer,
        Vector3 directionToOther,
        Vector3 reportedVelocity,
        bool isDashing,
        int impactSequence)
    {
        HexPlayerCollisionSettings settings =
            GetCollisionSettings();

        if (settings == null ||
            otherPlayer == null ||
            otherPlayer.Object == null ||
            impactSequence <= 0)
        {
            return false;
        }

        int otherPlayerId =
            otherPlayer.Object.InputAuthority.PlayerId;

        if (localImpactCooldownExpiryTimes.TryGetValue(
                otherPlayerId,
                out float cooldownExpiryTime) &&
            Time.unscaledTime < cooldownExpiryTime)
        {
            return false;
        }

        if (
            !HexPlayerImpactResolver.TryResolve(
                settings,
                reportedVelocity,
                otherPlayer.CollisionVelocity,
                directionToOther,
                CurrentMass,
                otherPlayer.CurrentMass,
                isDashing,
                otherPlayer.IsDashImpactActive,
                out Vector3 resolvedPlanarVelocity,
                out _,
                out float verticalLift))
        {
            return false;
        }

        Vector3 currentVelocity = body.linearVelocity;
        Vector3 predictedVelocity = new Vector3(
            resolvedPlanarVelocity.x,
            currentVelocity.y + verticalLift,
            resolvedPlanarVelocity.z
        );
        body.linearVelocity = predictedVelocity;
        SimulatedVelocity = predictedVelocity;
        predictedImpacts[impactSequence] =
            new LocalPredictedImpact
            {
                OtherPlayer =
                    otherPlayer.Object.InputAuthority,
                ResolvedPlanarVelocity =
                    resolvedPlanarVelocity,
                VerticalLift = verticalLift,
                ExpiryTime =
                    Time.unscaledTime +
                    settings.PredictionLifetimeSeconds
            };
        localImpactCooldownExpiryTimes[otherPlayerId] =
            Time.unscaledTime +
            Mathf.Max(
                settings.ImpactCooldownSeconds,
                settings.PredictiveContactSuppressionSeconds
            );
        StartImpactRecovery(predictedVelocity);
        return true;
    }

    private int CreateImpactSequence()
    {
        nextImpactSequence++;

        if (nextImpactSequence <= 0)
            nextImpactSequence = 1;

        return nextImpactSequence;
    }

    private void QueueImpactCorrection(
        Vector3 correction,
        float durationSeconds)
    {
        HexPlayerCollisionSettings settings =
            GetCollisionSettings();

        if (settings == null ||
            correction.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        pendingImpactVelocityCorrection =
            Vector3.ClampMagnitude(
                pendingImpactVelocityCorrection +
                correction,
                settings.MaximumReconciliationVelocityChange
            );
        impactCorrectionTimeRemaining = Mathf.Max(
            impactCorrectionTimeRemaining,
            Mathf.Max(0.01f, durationSeconds)
        );
    }

    private void ApplyPendingImpactCorrection(float deltaTime)
    {
        if (pendingImpactVelocityCorrection.sqrMagnitude <=
                0.000001f ||
            impactCorrectionTimeRemaining <= 0f)
        {
            pendingImpactVelocityCorrection = Vector3.zero;
            impactCorrectionTimeRemaining = 0f;
            return;
        }

        float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
        float appliedFraction = Mathf.Clamp01(
            safeDeltaTime /
            Mathf.Max(safeDeltaTime, impactCorrectionTimeRemaining)
        );
        Vector3 correctionStep =
            pendingImpactVelocityCorrection *
            appliedFraction;
        body.linearVelocity += correctionStep;
        pendingImpactVelocityCorrection -= correctionStep;
        impactCorrectionTimeRemaining -= safeDeltaTime;

        if (impactCorrectionTimeRemaining <= 0f)
        {
            body.linearVelocity +=
                pendingImpactVelocityCorrection;
            pendingImpactVelocityCorrection = Vector3.zero;
            impactCorrectionTimeRemaining = 0f;
        }
    }

    private void CleanupExpiredPredictedImpacts()
    {
        if (predictedImpacts.Count == 0)
            return;

        predictedImpactKeysToRemove.Clear();

        foreach (KeyValuePair<int, LocalPredictedImpact> pair
                 in predictedImpacts)
        {
            if (pair.Value == null ||
                Time.unscaledTime > pair.Value.ExpiryTime)
            {
                predictedImpactKeysToRemove.Add(pair.Key);
            }
        }

        foreach (int predictionKey in predictedImpactKeysToRemove)
            predictedImpacts.Remove(predictionKey);
    }

    private void RemovePredictedImpactsForOther(
        PlayerRef otherPlayer)
    {
        if (predictedImpacts.Count == 0)
            return;

        predictedImpactKeysToRemove.Clear();

        foreach (KeyValuePair<int, LocalPredictedImpact> pair
                 in predictedImpacts)
        {
            if (pair.Value == null ||
                pair.Value.OtherPlayer == otherPlayer)
            {
                predictedImpactKeysToRemove.Add(pair.Key);
            }
        }

        foreach (int predictionKey in predictedImpactKeysToRemove)
            predictedImpacts.Remove(predictionKey);
    }

    private void ConfigurePhysicsForAuthority()
    {
        if (body == null || Object == null)
            return;

        bool hasStateAuthority = Object.HasStateAuthority;

        if (IsEliminated)
        {
            StopBodyMotion();
            body.isKinematic = true;
            body.useGravity = false;
            return;
        }

        ConfigureRollingMode(hasStateAuthority);

        if (!configurePhysicsByAuthority)
            return;

        body.isKinematic = !hasStateAuthority;
        body.useGravity = hasStateAuthority && stateAuthorityUsesGravity;
    }

    private void ConfigureRollingMode(bool hasStateAuthority)
    {
        if (ShouldDisablePhysicalRolling())
        {
            body.constraints =
                initialConstraints |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationY |
                RigidbodyConstraints.FreezeRotationZ;
            body.angularVelocity = Vector3.zero;
        }
        else
        {
            body.constraints = initialConstraints;
        }

        if (hasStateAuthority)
            ApplyFrictionlessSlidingMaterial();
    }

    private void UpdatePhysicalRolling()
    {
        if (body == null || ShouldDisablePhysicalRolling())
            return;

        Vector3 planarVelocity = body.linearVelocity;
        planarVelocity.y = 0f;
        float radius = Mathf.Max(0.01f, CollisionRadius);
        Vector3 rollingAngularVelocity =
            Vector3.Cross(Vector3.up, planarVelocity) / radius;

        body.angularVelocity = rollingAngularVelocity;
    }

    private void ApplyFrictionlessSlidingMaterial()
    {
        if (bodyCollider == null)
            return;

        if (slidingMaterial == null)
        {
            slidingMaterial = new PhysicsMaterial("Runtime Frictionless Ball")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        bodyCollider.sharedMaterial = slidingMaterial;
    }

    private void RestoreColliderMaterial()
    {
        if (bodyCollider != null &&
            bodyCollider.sharedMaterial == slidingMaterial)
        {
            bodyCollider.sharedMaterial = initialColliderMaterial;
        }
    }

    private float GetImpactControlMultiplier()
    {
        if (impactRecoveryTimer.ExpiredOrNotRunning(Runner))
            return 1f;

        HexPlayerCollisionSettings settings = GetCollisionSettings();

        return settings != null
            ? settings.ControlMultiplierDuringRecovery
            : 1f;
    }

    private void UpdateImpactRecovery(float deltaTime)
    {
        if (impactSpeedAllowance <= 0f)
            return;

        impactSpeedAllowance = Mathf.MoveTowards(
            impactSpeedAllowance,
            0f,
            Mathf.Max(0f, impactSpeedAllowanceDecay) *
            Mathf.Max(0f, deltaTime)
        );
    }

    private void ClampPlanarSpeed(
        float verticalVelocity,
        float speedLimit)
    {
        Vector3 velocity = body.linearVelocity;
        Vector3 planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
        speedLimit = Mathf.Max(0.1f, speedLimit);
        if (planarVelocity.sqrMagnitude <= speedLimit * speedLimit)
            return;

        planarVelocity = planarVelocity.normalized * speedLimit;
        body.linearVelocity = new Vector3(
            planarVelocity.x,
            verticalVelocity,
            planarVelocity.z
        );
    }

    private void RefreshGrowthState()
    {
        HexPlayerGrowthSettings settings = GetGrowthSettings();

        if (settings == null)
            return;

        int territoryCount = TerritoryCount;
        float uniformScale = settings.EvaluateUniformScale(territoryCount);

        transform.localScale =
            new Vector3(uniformScale, uniformScale, uniformScale);

        currentMaximumSpeed =
            settings.EvaluateMaximumSpeed(territoryCount);
        currentAcceleration =
            settings.EvaluateAcceleration(territoryCount);
        currentBraking =
            settings.EvaluateBraking(territoryCount);

        if (body != null)
            body.mass = settings.EvaluateMass(territoryCount);
    }

    private static bool ShouldDisablePhysicalRolling()
    {
        HexGameRulesSettings settings = GetGameRulesSettings();
        return settings == null || settings.DisablePhysicalRolling;
    }

    private void RefreshEliminationState()
    {
        bool isEliminated = IsEliminated;

        if (eliminationStateApplied &&
            appliedEliminatedState == isEliminated)
        {
            return;
        }

        eliminationStateApplied = true;
        appliedEliminatedState = isEliminated;

        if (bodyCollider != null)
            bodyCollider.enabled = !isEliminated;

        if (playerCharacter != null)
            playerCharacter.SetVisualsVisible(!isEliminated);

        if (isEliminated)
        {
            ResetImpactPredictionState();
            StopBodyMotion();

            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }

            if (Object != null && Object.HasInputAuthority)
                DisableInputActions();

            return;
        }

        ConfigurePhysicsForAuthority();

        if (Object != null && Object.HasInputAuthority)
            EnableInputActions();
    }

    private void StopBodyMotion()
    {
        if (body == null || body.isKinematic)
            return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void ResetImpactPredictionState()
    {
        predictedImpacts.Clear();
        predictedImpactKeysToRemove.Clear();
        localImpactCooldownExpiryTimes.Clear();
        pendingImpactVelocityCorrection = Vector3.zero;
        impactCorrectionTimeRemaining = 0f;
        impactSpeedAllowance = 0f;
        impactSpeedAllowanceDecay = 0f;
        impactRecoveryTimer = default;
    }

    private void RegisterCollisionReferences()
    {
        ActivePlayers.Add(this);

        if (bodyCollider != null)
            PlayersByCollider[bodyCollider] = this;

        if (body != null)
            PlayersByRigidbody[body] = this;
    }

    private void UnregisterCollisionReferences()
    {
        ActivePlayers.Remove(this);

        if (bodyCollider != null &&
            PlayersByCollider.TryGetValue(
                bodyCollider,
                out HexBallPlayerController colliderOwner) &&
            colliderOwner == this)
        {
            PlayersByCollider.Remove(bodyCollider);
        }

        if (body != null &&
            PlayersByRigidbody.TryGetValue(
                body,
                out HexBallPlayerController rigidbodyOwner) &&
            rigidbodyOwner == this)
        {
            PlayersByRigidbody.Remove(body);
        }
    }

    private static HexTerritoryManager GetTerritoryManager()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.HexTerritory
            : null;
    }

    private static HexStrengthCombatManager GetCombatManager()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.HexStrengthCombat
            : null;
    }

    private static HexPlayerGrowthSettings GetGrowthSettings()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.PlayerGrowthSettings
            : null;
    }

    private static HexGameRulesSettings GetGameRulesSettings()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.GameRulesSettings
            : null;
    }

    private static HexPlayerCollisionSettings GetCollisionSettings()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.PlayerCollisionSettings
            : null;
    }

    private void RegisterLocalInput()
    {
        if (registeredRunner != null || Runner == null)
            return;

        registeredRunner = Runner;
        registeredRunner.ProvideInput = true;
        registeredRunner.AddCallbacks(this);
        EnableInputActions();
    }

    private void UnregisterLocalInput()
    {
        DisableInputActions();

        if (registeredRunner != null)
            registeredRunner.RemoveCallbacks(this);

        registeredRunner = null;
    }

    private void CreateFallbackMoveAction()
    {
        fallbackMoveAction = new InputAction(
            "Hex Ball Move",
            InputActionType.Value,
            expectedControlType: "Vector2"
        );

        fallbackMoveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        fallbackMoveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        fallbackMoveAction.AddBinding("<Gamepad>/leftStick");
    }

    private void EnableInputActions()
    {
        if (localInputEnabled)
            return;

        if (moveAction != null && moveAction.action != null)
            moveAction.action.Enable();

        if (jumpAction != null && jumpAction.action != null)
            jumpAction.action.Enable();
        
        if (dashAction != null && dashAction.action != null)
            dashAction.action.Enable();

        fallbackMoveAction?.Enable();
        localInputEnabled = true;
    }

    private void DisableInputActions()
    {
        if (!localInputEnabled)
            return;

        if (moveAction != null && moveAction.action != null)
            moveAction.action.Disable();

        if (jumpAction != null && jumpAction.action != null)
            jumpAction.action.Disable();
        
        if (dashAction != null && dashAction.action != null)
            dashAction.action.Disable();

        fallbackMoveAction?.Disable();
        localInputEnabled = false;
    }

    private Vector2 ReadMoveInput()
    {
        InputAction action = moveAction != null && moveAction.action != null
            ? moveAction.action
            : fallbackMoveAction;

        return action == null
            ? Vector2.zero
            : Vector2.ClampMagnitude(action.ReadValue<Vector2>(), 1f);
    }

    public void OnInput(NetworkRunner callbackRunner, NetworkInput input)
    {
        HexBallInputData inputData = default;

        if (!GameInputBlocker.IsGameplayInputBlocked)
        {
            Vector2 moveInput = ReadMoveInput();

            if (cameraRig != null)
                moveInput = cameraRig.ConvertMoveInputToWorld(moveInput);

            inputData.Move = moveInput;
            inputData.Buttons.Set(
                HexBallInputButton.Jump,
                ReadJumpInput()
            );
            
            inputData.Buttons.Set(
                HexBallInputButton.Dash,
                ReadDashInput()
            );
        }

        input.Set(inputData);
    }

    private bool ReadJumpInput()
    {
        return jumpAction != null &&
               jumpAction.action != null &&
               jumpAction.action.IsPressed();
    }

    private bool ReadDashInput()
    {
        if (dashAction != null && dashAction.action != null)
            return dashAction.action.IsPressed();
        
        return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
    }

    public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input)
    {
        input.Set(new HexBallInputData());
    }

    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player) { }
    public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner callbackRunner) { }
    public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(
        NetworkRunner callbackRunner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token) { }
    public void OnConnectFailed(
        NetworkRunner callbackRunner,
        NetAddress remoteAddress,
        NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(
        NetworkRunner callbackRunner,
        SimulationMessagePtr message) { }
    public void OnSessionListUpdated(
        NetworkRunner callbackRunner,
        List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(
        NetworkRunner callbackRunner,
        Dictionary<string, object> data) { }
    public void OnHostMigration(
        NetworkRunner callbackRunner,
        HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner callbackRunner) { }
    public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
    public void OnObjectEnterAOI(
        NetworkRunner callbackRunner,
        NetworkObject obj,
        PlayerRef player) { }
    public void OnObjectExitAOI(
        NetworkRunner callbackRunner,
        NetworkObject obj,
        PlayerRef player) { }
    public void OnReliableDataReceived(
        NetworkRunner callbackRunner,
        PlayerRef player,
        ReliableKey key,
        ArraySegment<byte> data) { }
    public void OnReliableDataProgress(
        NetworkRunner callbackRunner,
        PlayerRef player,
        ReliableKey key,
        float progress) { }
}
