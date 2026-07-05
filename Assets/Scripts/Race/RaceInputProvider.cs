using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

public class RaceInputProvider : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Optional Input Actions")]
    [SerializeField] private InputActionReference steerAction;
    [SerializeField] private InputActionReference accelerateAction;
    [SerializeField] private InputActionReference brakeAction;
    [SerializeField] private InputActionReference handbrakeAction;

    [Header("Defaults")]
    [SerializeField] private bool createDefaultActions = true;

    private NetworkRunner registeredRunner;
    private InputAction steerFallbackAction;
    private InputAction accelerateFallbackAction;
    private InputAction brakeFallbackAction;
    private InputAction handbrakeFallbackAction;

    private void Awake()
    {
        if (createDefaultActions)
            CreateFallbackActions();
    }

    private void OnEnable()
    {
        EnableActions();
        TryRegisterRunner();
    }

    private void Start()
    {
        TryRegisterRunner();
    }

    private void Update()
    {
        if (registeredRunner == null)
            TryRegisterRunner();
    }

    private void OnDisable()
    {
        DisableActions();

        if (registeredRunner != null)
        {
            registeredRunner.RemoveCallbacks(this);
            registeredRunner = null;
        }
    }

    private void OnDestroy()
    {
        DisposeFallbackActions();
    }

    private void TryRegisterRunner()
    {
        if (registeredRunner != null)
            return;

        NetworkRunner runner = FindAnyObjectByType<NetworkRunner>();

        if (runner == null)
            return;

        registeredRunner = runner;
        registeredRunner.ProvideInput = true;
        registeredRunner.AddCallbacks(this);
    }

    private void CreateFallbackActions()
    {
        steerFallbackAction = new InputAction("Race Steer", InputActionType.Value, expectedControlType: "Axis");
        steerFallbackAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Positive", "<Keyboard>/d");
        steerFallbackAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/leftArrow")
            .With("Positive", "<Keyboard>/rightArrow");
        steerFallbackAction.AddBinding("<Gamepad>/leftStick/x");

        accelerateFallbackAction = new InputAction("Race Accelerate", InputActionType.Button);
        accelerateFallbackAction.AddBinding("<Keyboard>/w");
        accelerateFallbackAction.AddBinding("<Keyboard>/upArrow");
        accelerateFallbackAction.AddBinding("<Gamepad>/rightTrigger");

        brakeFallbackAction = new InputAction("Race Brake", InputActionType.Button);
        brakeFallbackAction.AddBinding("<Keyboard>/s");
        brakeFallbackAction.AddBinding("<Keyboard>/downArrow");
        brakeFallbackAction.AddBinding("<Gamepad>/leftTrigger");

        handbrakeFallbackAction = new InputAction("Race Handbrake", InputActionType.Button);
        handbrakeFallbackAction.AddBinding("<Keyboard>/space");
        handbrakeFallbackAction.AddBinding("<Gamepad>/buttonSouth");
    }

    private void EnableActions()
    {
        EnableAction(steerAction);
        EnableAction(accelerateAction);
        EnableAction(brakeAction);
        EnableAction(handbrakeAction);

        steerFallbackAction?.Enable();
        accelerateFallbackAction?.Enable();
        brakeFallbackAction?.Enable();
        handbrakeFallbackAction?.Enable();
    }

    private void DisableActions()
    {
        DisableAction(steerAction);
        DisableAction(accelerateAction);
        DisableAction(brakeAction);
        DisableAction(handbrakeAction);

        steerFallbackAction?.Disable();
        accelerateFallbackAction?.Disable();
        brakeFallbackAction?.Disable();
        handbrakeFallbackAction?.Disable();
    }

    private void DisposeFallbackActions()
    {
        steerFallbackAction?.Dispose();
        accelerateFallbackAction?.Dispose();
        brakeFallbackAction?.Dispose();
        handbrakeFallbackAction?.Dispose();
    }

    private static void EnableAction(InputActionReference actionReference)
    {
        if (actionReference != null && actionReference.action != null)
            actionReference.action.Enable();
    }

    private static void DisableAction(InputActionReference actionReference)
    {
        if (actionReference != null && actionReference.action != null)
            actionReference.action.Disable();
    }

    private float ReadAxis(InputActionReference actionReference, InputAction fallbackAction)
    {
        InputAction action = actionReference != null && actionReference.action != null
            ? actionReference.action
            : fallbackAction;

        return action == null ? 0f : Mathf.Clamp(action.ReadValue<float>(), -1f, 1f);
    }

    private float ReadPositive(InputActionReference actionReference, InputAction fallbackAction)
    {
        InputAction action = actionReference != null && actionReference.action != null
            ? actionReference.action
            : fallbackAction;

        return action == null ? 0f : Mathf.Clamp01(action.ReadValue<float>());
    }

    private bool ReadButton(InputActionReference actionReference, InputAction fallbackAction)
    {
        InputAction action = actionReference != null && actionReference.action != null
            ? actionReference.action
            : fallbackAction;

        return action != null && action.IsPressed();
    }

    public void OnInput(NetworkRunner callbackRunner, NetworkInput input)
    {
        RaceInputData data = new RaceInputData();

        if (!GameInputBlocker.IsGameplayInputBlocked)
        {
            data.Steering = ReadAxis(steerAction, steerFallbackAction);
            data.Throttle = ReadPositive(accelerateAction, accelerateFallbackAction);
            data.Brake = ReadPositive(brakeAction, brakeFallbackAction);
            data.Buttons.Set(RaceInputButton.Handbrake, ReadButton(handbrakeAction, handbrakeFallbackAction));
        }

        input.Set(data);
    }

    public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input)
    {
        input.Set(new RaceInputData());
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
