using System.Collections.Generic;
using UnityEngine;

public class CameraOcclusionController : MonoBehaviour
{
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private LayerMask cameraObstacleLayer;

    [SerializeField] private float sphereRadius = 0.35f;
    [SerializeField] private float stopBeforePlayer = 0.6f;

    [SerializeField] private float cutoutRadius = 150f;
    [SerializeField] private float cutoutSoftness = 30f;

    private readonly RaycastHit[] hits =
        new RaycastHit[64];

    private readonly HashSet<HexOcclusionTarget>
        occludedTargets = new HashSet<HexOcclusionTarget>();

    private static readonly HashSet<CameraOcclusionController>
        ActiveControllers =
            new HashSet<CameraOcclusionController>();

    private static Transform localPlayerTarget;
    private Transform playerTarget;

    private void OnEnable()
    {
        ActiveControllers.Add(this);
        SetPlayerTarget(localPlayerTarget);
    }

    private void OnDisable()
    {
        DisablePreviousTargets();
        ActiveControllers.Remove(this);
    }

    private void LateUpdate()
    {
        DisablePreviousTargets();

        if (gameplayCamera == null)
        {
            return;
        }

        if (playerTarget == null)
        {
            return;
        }

        Vector3 cameraPosition =
            gameplayCamera.transform.position;

        Vector3 playerPosition =
            playerTarget.position;

        Vector3 directionToPlayer =
            playerPosition - cameraPosition;

        float fullDistance =
            directionToPlayer.magnitude;

        if (fullDistance <= 0.01f)
        {
            return;
        }

        float castDistance =
            fullDistance - stopBeforePlayer;

        if (castDistance <= 0f)
        {
            return;
        }

        directionToPlayer.Normalize();

        int hitCount = Physics.SphereCastNonAlloc(
            cameraPosition,
            sphereRadius,
            directionToPlayer,
            hits,
            castDistance,
            cameraObstacleLayer,
            QueryTriggerInteraction.Collide);

        Vector3 playerScreenPosition =
            gameplayCamera.WorldToScreenPoint(
                playerPosition);

        if (playerScreenPosition.z <= 0f)
        {
            return;
        }

        Vector2 screenPosition =
            new Vector2(
                playerScreenPosition.x,
                playerScreenPosition.y);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider =
                hits[i].collider;

            if (hitCollider == null)
            {
                continue;
            }

            if (!HexOcclusionTarget.TryResolve(
                    hitCollider,
                    out HexOcclusionTarget target))
            {
                continue;
            }

            bool wasAdded =
                occludedTargets.Add(target);

            if (!wasAdded)
            {
                continue;
            }

            target.SetOccluded(
                true,
                screenPosition,
                cutoutRadius,
                cutoutSoftness);
        }
    }

    public static void SetLocalPlayerTarget(
        Transform newPlayerTarget)
    {
        localPlayerTarget = newPlayerTarget;

        foreach (CameraOcclusionController controller
                 in ActiveControllers)
        {
            if (controller != null)
                controller.SetPlayerTarget(newPlayerTarget);
        }
    }

    public static void ClearLocalPlayerTarget(
        Transform currentPlayerTarget)
    {
        if (localPlayerTarget != currentPlayerTarget)
            return;

        SetLocalPlayerTarget(null);
    }

    private void DisablePreviousTargets()
    {
        foreach (HexOcclusionTarget target
                 in occludedTargets)
        {
            if (target == null)
            {
                continue;
            }

            target.SetOccluded(
                false,
                Vector2.zero,
                0f,
                0f);
        }

        occludedTargets.Clear();
    }

    private void SetPlayerTarget(Transform newPlayerTarget)
    {
        if (playerTarget == newPlayerTarget)
            return;

        DisablePreviousTargets();
        playerTarget = newPlayerTarget;
    }
}
