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
    [SerializeField, Range(1, 3)] private int maxJumpCount = 2; 
    [SerializeField] private BallJumpVFX jumpVFX;

    [Header("Hex Capture")]
    [SerializeField, Min(0.05f)] private float captureRetrySeconds = 0.15f;

    [Header("Strength Combat")]
    [SerializeField, Min(0.02f)] private float contactReportInterval = 0.1f;

    [Header("New Input System")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private bool createDefaultMoveAction = true;

    [Header("Camera Relative Movement")]
    [SerializeField] private NetworkThirdPersonCameraRig cameraRig;

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
    private float movementBoostMultiplier = 1f;
    private readonly Dictionary<int, TickTimer> contactReportCooldowns =
        new Dictionary<int, TickTimer>();

    private static readonly Dictionary<Collider, HexBallPlayerController>
        PlayersByCollider =
            new Dictionary<Collider, HexBallPlayerController>();

    private static readonly Dictionary<Rigidbody, HexBallPlayerController>
        PlayersByRigidbody =
            new Dictionary<Rigidbody, HexBallPlayerController>();

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

        previousButtons = input.Buttons;

        if (jumpPressed)
            TryJump(grounded);

        Move(input.Move, Runner.DeltaTime);
        UpdatePhysicalRolling();

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

        float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
        Vector3 requestedAcceleration =
            (desiredVelocity - currentPlanarVelocity) / safeDeltaTime;

        body.AddForce(
            Vector3.ClampMagnitude(requestedAcceleration, response),
            ForceMode.Acceleration
        );

        ClampPlanarSpeed(currentVelocity.y);
    }

    private void TryJump(bool grounded)
    {
        if (grounded && body.linearVelocity.y <= 0.1f)
            jumpsUsed = 0;
        
        if (!grounded && jumpsUsed == 0)
            jumpsUsed = 1;

        if (jumpsUsed >= Mathf.Max(1, maxJumpCount))
            return;

        jumpsUsed++;

        Vector3 velocity = body.linearVelocity;
        velocity.y = Mathf.Max(0.1f, jumpSpeed);
        body.linearVelocity = velocity;

        if (jumpsUsed >= 2)
            jumpVFX?.PlayDoubleJump();
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
        
        jumpsUsed = Mathf.Max(0, maxJumpCount - 1);
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
        territoryManager.RequestCapture(coordinate);
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

        if (contactReportCooldowns.TryGetValue(
                cooldownKey,
                out TickTimer cooldown) &&
            !cooldown.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        HexStrengthCombatManager combatManager = GetCombatManager();

        if (combatManager == null)
            return;

        contactReportCooldowns[cooldownKey] = TickTimer.CreateFromSeconds(
            Runner,
            Mathf.Max(0.02f, contactReportInterval)
        );
        combatManager.RequestContact(otherPlayerRef);
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

    public void RequestMassPush(
        Vector3 direction,
        float pushingMass,
        float durationSeconds)
    {
        HexPlayerGrowthSettings settings = GetGrowthSettings();
        float ownMass = CurrentMass;

        if (settings == null ||
            Object == null ||
            IsEliminated ||
            ownMass <= 0f ||
            pushingMass / ownMass < settings.MinimumMassRatioToPush)
        {
            return;
        }

        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        float normalizedMassAdvantage =
            1f - Mathf.Clamp01(ownMass / pushingMass);
        float velocityChange =
            settings.MaximumPushVelocityChangePerSecond *
            normalizedMassAdvantage *
            Mathf.Max(0f, durationSeconds);

        if (velocityChange <= 0f)
            return;

        RPC_ApplyMassPush(direction.normalized * velocityChange);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ApplyMassPush(Vector3 velocityChange)
    {
        if (body == null ||
            Object == null ||
            !Object.HasStateAuthority ||
            IsEliminated)
        {
            return;
        }

        velocityChange.y = 0f;
        body.AddForce(velocityChange, ForceMode.VelocityChange);
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

    private void ClampPlanarSpeed(float verticalVelocity)
    {
        Vector3 velocity = body.linearVelocity;
        Vector3 planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
        float speedLimit = Mathf.Max(0.1f, GetActiveMaximumSpeed());
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

    private void RegisterCollisionReferences()
    {
        if (bodyCollider != null)
            PlayersByCollider[bodyCollider] = this;

        if (body != null)
            PlayersByRigidbody[body] = this;
    }

    private void UnregisterCollisionReferences()
    {
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
        }

        input.Set(inputData);
    }

    private bool ReadJumpInput()
    {
        return jumpAction != null &&
               jumpAction.action != null &&
               jumpAction.action.IsPressed();
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
