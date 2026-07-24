using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class NetworkThirdPersonCameraRig : NetworkBehaviour
{
    [Header("Required References")]
    [Tooltip("The network player that the camera follows. Uses this Transform when left empty.")]
    [SerializeField] private Transform player;
    [Tooltip("A child object containing the pivots and Cinemachine Camera. Do not assign PlayerPrefab itself.")]
    [SerializeField] private GameObject localCameraRoot;
    [Tooltip("The horizontal rotation pivot. This can be the Local Camera Root Transform.")]
    [SerializeField] private Transform yawPivot;
    [Tooltip("A child of Yaw Pivot used as the Cinemachine Tracking Target.")]
    [SerializeField] private Transform pitchPivot;

    [Header("Input (New Input System)")]
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private bool createDefaultLookAction = true;

    [Header("Follow")]
    [Tooltip("World-space offset from the center of the rolling ball.")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Look")]
    [SerializeField, Min(0.001f)] private float mouseSensitivityX = 0.12f;
    [SerializeField, Min(0.001f)] private float mouseSensitivityY = 0.12f;
    [SerializeField, Min(1f)] private float gamepadDegreesPerSecondX = 180f;
    [SerializeField, Min(1f)] private float gamepadDegreesPerSecondY = 140f;
    [SerializeField] private float minimumPitch = -30f;
    [SerializeField] private float maximumPitch = 70f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorWhilePlaying = true;

    private InputAction fallbackLookAction;
    private bool isLocalPlayer;
    private bool inputIsEnabled;
    private bool cursorIsLocked;
    private float yaw;
    private float pitch;
    private Transform originalCameraRootParent;
    private Vector3 originalCameraRootLocalPosition;
    private Quaternion originalCameraRootLocalRotation;
    private Vector3 originalCameraRootLocalScale;
    private bool cameraRootIsDetached;

    private InputAction ActiveLookAction
    {
        get
        {
            if (lookAction != null && lookAction.action != null)
                return lookAction.action;

            return fallbackLookAction;
        }
    }

    private void Awake()
    {
        if (player == null)
            player = transform;

        if (createDefaultLookAction)
            CreateFallbackLookAction();

        if (localCameraRoot != null && localCameraRoot != gameObject)
        {
            RememberCameraRootTransform();
            localCameraRoot.SetActive(false);
        }
    }

    public override void Spawned()
    {
        isLocalPlayer = Object.HasInputAuthority;

        if (!isLocalPlayer)
        {
            SetCameraRootActive(false);
            return;
        }

        if (!ValidateReferences())
            return;

        InitializeRotation();
        DetachCameraRootFromRollingPlayer();
        SetCameraRootActive(true);
        EnableLookInput();
        UpdateCursorState();
        ApplyRigPose();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ShutDownLocalCamera();
    }

    private void LateUpdate()
    {
        if (!isLocalPlayer || player == null || yawPivot == null || pitchPivot == null)
            return;

        bool inputIsBlocked =
            GameInputBlocker.IsGameplayInputBlocked ||
            (GameSceneManager.Instance != null && GameSceneManager.Instance.IsGameEnded);

        UpdateCursorState();

        if (!inputIsBlocked)
            ReadLookInput();

        ApplyRigPose();
    }

    private void OnDisable()
    {
        if (isLocalPlayer)
            ShutDownLocalCamera();
    }

    private void OnDestroy()
    {
        DisableLookInput();

        fallbackLookAction?.Dispose();
        fallbackLookAction = null;
    }

    public Vector2 ConvertMoveInputToWorld(Vector2 moveInput)
    {
        if (moveInput.sqrMagnitude <= 0f)
            return Vector2.zero;

        Vector3 cameraRelativeDirection =
            Quaternion.Euler(0f, yaw, 0f) *
            new Vector3(moveInput.x, 0f, moveInput.y);

        return new Vector2(
            cameraRelativeDirection.x,
            cameraRelativeDirection.z
        );
    }

    private bool ValidateReferences()
    {
        if (localCameraRoot == null)
        {
            Debug.LogError(
                "NetworkThirdPersonCameraRig requires a Local Camera Root reference.",
                this
            );
            return false;
        }

        if (localCameraRoot == gameObject)
        {
            Debug.LogError(
                "Local Camera Root must be a child object, not PlayerPrefab itself.",
                this
            );
            return false;
        }

        if (yawPivot == null || pitchPivot == null)
        {
            Debug.LogError(
                "NetworkThirdPersonCameraRig requires both Yaw Pivot and Pitch Pivot.",
                this
            );
            return false;
        }

        if (ActiveLookAction == null)
        {
            Debug.LogError(
                "Assign a Look action or enable Create Default Look Action.",
                this
            );
            return false;
        }

        return true;
    }

    private void InitializeRotation()
    {
        yaw = yawPivot.rotation.eulerAngles.y;
        pitch = NormalizeAngle(pitchPivot.localRotation.eulerAngles.x);
        ClampPitchLimits();
        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
    }

    private void ReadLookInput()
    {
        InputAction action = ActiveLookAction;

        if (action == null)
            return;

        Vector2 lookDelta = action.ReadValue<Vector2>();

        if (action.activeControl != null && action.activeControl.device is Gamepad)
        {
            yaw += lookDelta.x * gamepadDegreesPerSecondX * Time.unscaledDeltaTime;
            pitch -= lookDelta.y * gamepadDegreesPerSecondY * Time.unscaledDeltaTime;
        }
        else
        {
            yaw += lookDelta.x * mouseSensitivityX;
            pitch -= lookDelta.y * mouseSensitivityY;
        }

        ClampPitchLimits();
        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
    }

    private void ApplyRigPose()
    {
        yawPivot.position = player.position + targetOffset;
        yawPivot.rotation = Quaternion.Euler(0f, yaw, 0f);
        pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void CreateFallbackLookAction()
    {
        fallbackLookAction = new InputAction(
            "Third Person Look",
            InputActionType.Value,
            expectedControlType: "Vector2"
        );

        fallbackLookAction.AddBinding("<Mouse>/delta");
        fallbackLookAction.AddBinding("<Gamepad>/rightStick");
    }

    private void EnableLookInput()
    {
        if (inputIsEnabled)
            return;

        ActiveLookAction?.Enable();
        inputIsEnabled = true;
    }

    private void DisableLookInput()
    {
        if (!inputIsEnabled)
            return;

        ActiveLookAction?.Disable();
        inputIsEnabled = false;
    }

    private void UpdateCursorState()
    {
        bool shouldLock =
            lockCursorWhilePlaying &&
            isLocalPlayer &&
            !GameInputBlocker.IsGameplayInputBlocked &&
            (GameSceneManager.Instance == null || !GameSceneManager.Instance.IsGameEnded);

        SetCursorLocked(shouldLock);
    }

    private void SetCursorLocked(bool shouldLock)
    {
        if (cursorIsLocked == shouldLock)
            return;

        cursorIsLocked = shouldLock;
        Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !shouldLock;
    }

    private void SetCameraRootActive(bool isActive)
    {
        if (localCameraRoot != null && localCameraRoot != gameObject)
            localCameraRoot.SetActive(isActive);
    }

    private void RememberCameraRootTransform()
    {
        Transform cameraRootTransform = localCameraRoot.transform;
        originalCameraRootParent = cameraRootTransform.parent;
        originalCameraRootLocalPosition = cameraRootTransform.localPosition;
        originalCameraRootLocalRotation = cameraRootTransform.localRotation;
        originalCameraRootLocalScale = cameraRootTransform.localScale;
    }

    private void DetachCameraRootFromRollingPlayer()
    {
        if (cameraRootIsDetached ||
            localCameraRoot == null ||
            localCameraRoot == gameObject)
        {
            return;
        }

        localCameraRoot.transform.SetParent(null, true);
        cameraRootIsDetached = true;
    }

    private void RestoreCameraRootParent()
    {
        if (!cameraRootIsDetached || localCameraRoot == null)
            return;

        Transform cameraRootTransform = localCameraRoot.transform;
        cameraRootTransform.SetParent(originalCameraRootParent, false);
        cameraRootTransform.localPosition = originalCameraRootLocalPosition;
        cameraRootTransform.localRotation = originalCameraRootLocalRotation;
        cameraRootTransform.localScale = originalCameraRootLocalScale;
        cameraRootIsDetached = false;
    }

    private void ShutDownLocalCamera()
    {
        DisableLookInput();
        SetCameraRootActive(false);
        RestoreCameraRootParent();

        if (cursorIsLocked)
            SetCursorLocked(false);

        isLocalPlayer = false;
    }

    private void ClampPitchLimits()
    {
        if (minimumPitch <= maximumPitch)
            return;

        (minimumPitch, maximumPitch) = (maximumPitch, minimumPitch);
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;

        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}
