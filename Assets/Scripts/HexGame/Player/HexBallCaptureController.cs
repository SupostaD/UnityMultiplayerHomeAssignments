using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HexBallCaptureController : MonoBehaviour
{
    [SerializeField] private Rigidbody body;
    [SerializeField, Min(0.05f)] private float captureRetrySeconds = 0.15f;

    private TickTimer captureRetryTimer;
    private HexCoord lastCaptureCoordinate;
    private bool hasLastCaptureCoordinate;
    private HexCoord lastSafeCoordinate;
    private bool hasLastSafeCoordinate;
    private bool missingTerritoryManagerLogged;

    private void Awake()
    {
        if (body == null)
        {
            Debug.LogError(
                "HexBallCaptureController: Body Rigidbody is not assigned.",
                this
            );
        }
    }

    public void TickCapture(
        NetworkRunner runner,
        PlayerRef inputAuthority,
        bool isTouchingGround)
    {
        HexTerritoryManager territoryManager = GetTerritoryManager();

        if (territoryManager == null)
        {
            LogMissingTerritoryManagerOnce();
            return;
        }

        missingTerritoryManagerLogged = false;

        if (!isTouchingGround || body == null)
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

        if (territoryManager.IsOwnedBy(coordinate, inputAuthority))
        {
            lastCaptureCoordinate = coordinate;
            hasLastCaptureCoordinate = true;
            return;
        }

        bool changedCoordinate =
            !hasLastCaptureCoordinate ||
            lastCaptureCoordinate != coordinate;

        if (!changedCoordinate &&
            !captureRetryTimer.ExpiredOrNotRunning(runner))
        {
            return;
        }

        lastCaptureCoordinate = coordinate;
        hasLastCaptureCoordinate = true;
        captureRetryTimer = TickTimer.CreateFromSeconds(
            runner,
            Mathf.Max(0.05f, captureRetrySeconds)
        );

        territoryManager.RequestCapture(
            coordinate,
            body.position,
            true
        );
    }

    public void RememberSafeHexAtPosition(Vector3 worldPosition)
    {
        HexTerritoryManager territoryManager = GetTerritoryManager();

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

    public bool TryGetReturnPosition(
        float collisionRadius,
        out Vector3 returnPosition)
    {
        returnPosition = default;

        if (!hasLastSafeCoordinate)
            return false;

        HexTerritoryManager territoryManager = GetTerritoryManager();

        if (territoryManager == null ||
            !territoryManager.TryGetTileTopCenter(
                lastSafeCoordinate,
                out Vector3 tileTopCenter))
        {
            return false;
        }

        HexGameRulesSettings rules = GetGameRulesSettings();
        float clearance = rules != null
            ? rules.DeathZoneReturnClearance
            : 0.1f;

        returnPosition =
            tileTopCenter +
            Vector3.up *
            (Mathf.Max(0.01f, collisionRadius) + clearance);
        return true;
    }

    public void ResetCaptureAttempt()
    {
        hasLastCaptureCoordinate = false;
        captureRetryTimer = default;
    }

    private void LogMissingTerritoryManagerOnce()
    {
        if (missingTerritoryManagerLogged)
            return;

        Debug.LogError(
            "HexBallCaptureController: Hex Territory Manager is not assigned on GameSceneManager.",
            this
        );
        missingTerritoryManagerLogged = true;
    }

    private static HexTerritoryManager GetTerritoryManager()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.HexTerritory
            : null;
    }

    private static HexGameRulesSettings GetGameRulesSettings()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.GameRulesSettings
            : null;
    }
}
