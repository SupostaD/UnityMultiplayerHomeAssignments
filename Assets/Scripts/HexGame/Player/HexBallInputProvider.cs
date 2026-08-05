using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class HexBallInputProvider : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Input Actions")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference dashAction;
    [SerializeField] private bool createDefaultMoveAction = true;

    [Header("Camera Relative Movement")]
    [SerializeField] private NetworkThirdPersonCameraRig cameraRig;

    private NetworkRunner registeredRunner;
    private InputAction fallbackMoveAction;
    private bool inputActionsEnabled;

    private void Awake()
    {
        ValidateReferences();

        if (createDefaultMoveAction)
            CreateFallbackMoveAction();
    }

    private void OnDisable()
    {
        SetGameplayInputEnabled(false);
    }

    private void OnDestroy()
    {
        Unregister();
        fallbackMoveAction?.Dispose();
        fallbackMoveAction = null;
    }

    public void Register(NetworkRunner runner)
    {
        if (registeredRunner != null || runner == null)
            return;

        registeredRunner = runner;
        registeredRunner.ProvideInput = true;
        registeredRunner.AddCallbacks(this);
        SetGameplayInputEnabled(true);
    }

    public void Unregister()
    {
        SetGameplayInputEnabled(false);

        if (registeredRunner != null)
            registeredRunner.RemoveCallbacks(this);

        registeredRunner = null;
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        if (inputActionsEnabled == enabled)
            return;

        SetActionEnabled(moveAction, enabled);
        SetActionEnabled(jumpAction, enabled);
        SetActionEnabled(dashAction, enabled);

        if (fallbackMoveAction != null)
        {
            if (enabled)
                fallbackMoveAction.Enable();
            else
                fallbackMoveAction.Disable();
        }

        inputActionsEnabled = enabled;
    }

    public void OnInput(NetworkRunner callbackRunner, NetworkInput input)
    {
        HexBallInputData inputData = default;

        if (inputActionsEnabled &&
            !GameInputBlocker.IsGameplayInputBlocked)
        {
            Vector2 moveInput = ReadMoveInput();

            if (cameraRig != null)
                moveInput = cameraRig.ConvertMoveInputToWorld(moveInput);

            inputData.Move = moveInput;
            inputData.Buttons.Set(
                HexBallInputButton.Jump,
                IsActionPressed(jumpAction)
            );
            inputData.Buttons.Set(
                HexBallInputButton.Dash,
                IsDashPressed()
            );
        }

        input.Set(inputData);
    }

    public void OnInputMissing(
        NetworkRunner callbackRunner,
        PlayerRef player,
        NetworkInput input)
    {
        input.Set(new HexBallInputData());
    }

    private void ValidateReferences()
    {
        if (cameraRig == null)
        {
            Debug.LogError(
                "HexBallInputProvider: Camera Rig is not assigned.",
                this
            );
        }

        if (jumpAction == null || jumpAction.action == null)
        {
            Debug.LogError(
                "HexBallInputProvider: Jump Action is not assigned.",
                this
            );
        }
    }

    private static void SetActionEnabled(
        InputActionReference actionReference,
        bool enabled)
    {
        if (actionReference == null || actionReference.action == null)
            return;

        if (enabled)
            actionReference.action.Enable();
        else
            actionReference.action.Disable();
    }

    private static bool IsActionPressed(
        InputActionReference actionReference)
    {
        return actionReference != null &&
               actionReference.action != null &&
               actionReference.action.IsPressed();
    }

    private Vector2 ReadMoveInput()
    {
        InputAction action =
            moveAction != null && moveAction.action != null
                ? moveAction.action
                : fallbackMoveAction;

        return action == null
            ? Vector2.zero
            : Vector2.ClampMagnitude(
                action.ReadValue<Vector2>(),
                1f
            );
    }

    private bool IsDashPressed()
    {
        if (IsActionPressed(dashAction))
            return true;

        return (dashAction == null || dashAction.action == null) &&
               Keyboard.current != null &&
               Keyboard.current.leftShiftKey.isPressed;
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

    public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player) { }
    public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner callbackRunner) { }
    public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner callbackRunner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner callbackRunner) { }
    public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
    public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, float progress) { }
}
