using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-800)]
public class HexOcclusionTarget : MonoBehaviour
{
    [SerializeField] private float fadeSpeed = 6f;

    private static readonly Dictionary<Collider, HexOcclusionTarget>
        TargetsByCollider =
            new Dictionary<Collider, HexOcclusionTarget>();

    private static readonly int OcclusionAmountId =
        Shader.PropertyToID("_OcclusionAmount");

    private static readonly int PlayerScreenPositionId =
        Shader.PropertyToID("_PlayerScreenPosition");

    private static readonly int CutoutRadiusId =
        Shader.PropertyToID("_CutoutRadius");

    private static readonly int CutoutSoftnessId =
        Shader.PropertyToID("_CutoutSoftness");

    private MaterialPropertyBlock propertyBlock;
    private Renderer targetRenderer;
    private Collider targetCollider;

    private float currentAmount;
    private float targetAmount;

    private Vector2 playerScreenPosition;
    private float cutoutRadius;
    private float cutoutSoftness;

    private void Awake()
    {
        if (!BindReferencesFromTile())
        {
            enabled = false;
            return;
        }

        propertyBlock = new MaterialPropertyBlock();

        currentAmount = 0f;
        targetAmount = 0f;

        enabled = false;
    }

    private void OnDestroy()
    {
        if (targetCollider != null &&
            TargetsByCollider.TryGetValue(
                targetCollider,
                out HexOcclusionTarget registeredTarget) &&
            registeredTarget == this)
        {
            TargetsByCollider.Remove(targetCollider);
        }
    }

    public static bool TryResolve(
        Collider sourceCollider,
        out HexOcclusionTarget target)
    {
        target = null;

        return sourceCollider != null &&
               TargetsByCollider.TryGetValue(sourceCollider, out target) &&
               target != null;
    }

    private void Update()
    {
        currentAmount = Mathf.MoveTowards(
            currentAmount,
            targetAmount,
            fadeSpeed * Time.deltaTime);

        ApplyProperties();

        if (Mathf.Approximately(
            currentAmount,
            targetAmount))
        {
            enabled = false;
        }
    }

    public void SetOccluded(
        bool isOccluded,
        Vector2 screenPosition,
        float radius,
        float softness)
    {
        if (isOccluded)
        {
            playerScreenPosition = screenPosition;
            cutoutRadius = radius;
            cutoutSoftness = softness;

            targetAmount = 1f;
        }
        else
        {
            targetAmount = 0f;
        }

        enabled = true;
    }

    private void ApplyProperties()
    {
        if (targetRenderer == null)
            return;

        targetRenderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetFloat(
            OcclusionAmountId,
            currentAmount);

        propertyBlock.SetVector(
            PlayerScreenPositionId,
            new Vector4(
                playerScreenPosition.x,
                playerScreenPosition.y,
                0f,
                0f));

        propertyBlock.SetFloat(
            CutoutRadiusId,
            cutoutRadius);

        propertyBlock.SetFloat(
            CutoutSoftnessId,
            cutoutSoftness);

        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    private bool BindReferencesFromTile()
    {
        if (!HexTile.TryGetForTransform(transform, out HexTile tile))
        {
            Debug.LogError(
                "HexOcclusionTarget: A HexTile must be placed on the same object.",
                this
            );
            return false;
        }

        targetRenderer = tile.TileRenderer;
        targetCollider = tile.TileCollider;

        if (targetRenderer == null)
        {
            Debug.LogError(
                "HexOcclusionTarget: Tile Renderer is not assigned in HexTile.",
                this
            );
            return false;
        }

        if (targetCollider == null)
        {
            Debug.LogError(
                "HexOcclusionTarget: Tile Collider is not assigned in HexTile.",
                this
            );
            return false;
        }

        TargetsByCollider[targetCollider] = this;
        return true;
    }
}
