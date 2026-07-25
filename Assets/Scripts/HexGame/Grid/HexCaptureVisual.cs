using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-900)]
public class HexCaptureVisual : MonoBehaviour
{
    private static readonly int OldColorId =
        Shader.PropertyToID("_OldColor");

    private static readonly int NewColorId =
        Shader.PropertyToID("_NewColor");

    private static readonly int ProgressId =
        Shader.PropertyToID("_Progress");

    private static readonly int ImpactPointId =
        Shader.PropertyToID("_ImpactPoint");

    private static readonly int FillModeId =
        Shader.PropertyToID("_FillMode");

    private static readonly int MaxRadiusId =
        Shader.PropertyToID("_MaxRadius");

    private MaterialPropertyBlock propertyBlock;
    private Renderer targetRenderer;
    private float captureDuration = 0.45f;
    private float hexRadius = 0.5f;
    private Color currentColor = Color.gray;
    private Coroutine captureCoroutine;

    private void Awake()
    {
        if (!BindRendererFromTile())
            return;

        ApplyProperties(
            currentColor,
            currentColor,
            Vector2.zero,
            0f,
            1f,
            1f);
    }

    public void Configure(HexTerritoryCaptureSettings settings)
    {
        if (settings == null)
        {
            Debug.LogError(
                "HexCaptureVisual: Territory Capture Settings is not assigned.",
                this
            );
            return;
        }

        captureDuration = settings.CaptureAnimationDuration;
        hexRadius = settings.VisualHexRadius;
    }

    private bool BindRendererFromTile()
    {
        if (!HexTile.TryGetForTransform(transform, out HexTile tile))
        {
            Debug.LogError(
                "HexCaptureVisual: A HexTile must be placed on the same object.",
                this
            );
            return false;
        }

        targetRenderer = tile.TileRenderer;

        if (targetRenderer == null)
        {
            Debug.LogError(
                "HexCaptureVisual: Tile Renderer is not assigned in HexTile.",
                this
            );
            return false;
        }

        tile.BindCaptureVisual(this);
        return true;
    }

    public void CaptureFromPlayer(
        Color newColor,
        Vector3 playerWorldPosition)
    {
        if (!EnsureRenderer())
            return;

        Vector3 localPosition =
            targetRenderer.transform.InverseTransformPoint(
                playerWorldPosition);

        CaptureFromPlayer(
            newColor,
            new Vector2(localPosition.x, localPosition.z)
        );
    }

    public void CaptureFromPlayer(
        Color newColor,
        Vector2 localImpactPoint)
    {
        if (!EnsureRenderer())
            return;

        float maxRadius =
            CalculateMaxCornerDistance(localImpactPoint);

        StartCapture(
            newColor,
            localImpactPoint,
            0f,
            maxRadius,
            0f);
    }

    public void CaptureFromCorners(
        Color newColor,
        float delay = 0f)
    {
        if (!EnsureRenderer())
            return;

        StartCapture(
            newColor,
            Vector2.zero,
            1f,
            hexRadius,
            delay);
    }

    public void SetColorImmediate(Color color)
    {
        if (!EnsureRenderer())
            return;

        if (captureCoroutine != null)
        {
            StopCoroutine(captureCoroutine);
            captureCoroutine = null;
        }

        currentColor = color;

        ApplyProperties(
            color,
            color,
            Vector2.zero,
            0f,
            1f,
            1f);
    }

    private void StartCapture(
        Color newColor,
        Vector2 impactPoint,
        float fillMode,
        float maxRadius,
        float delay)
    {
        if (captureCoroutine != null)
        {
            StopCoroutine(captureCoroutine);
        }

        captureCoroutine = StartCoroutine(
            CaptureRoutine(
                newColor,
                impactPoint,
                fillMode,
                maxRadius,
                delay));
    }

    private IEnumerator CaptureRoutine(
        Color newColor,
        Vector2 impactPoint,
        float fillMode,
        float maxRadius,
        float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        float elapsedTime = 0f;

        while (elapsedTime < captureDuration)
        {
            elapsedTime += Time.deltaTime;

            float progress =
                Mathf.Clamp01(elapsedTime / captureDuration);

            ApplyProperties(
                currentColor,
                newColor,
                impactPoint,
                fillMode,
                maxRadius,
                progress);

            yield return null;
        }

        currentColor = newColor;

        ApplyProperties(
            currentColor,
            currentColor,
            impactPoint,
            fillMode,
            maxRadius,
            1f);

        captureCoroutine = null;
    }

    private void ApplyProperties(
        Color oldColor,
        Color newColor,
        Vector2 impactPoint,
        float fillMode,
        float maxRadius,
        float progress)
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        targetRenderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetColor(OldColorId, oldColor);
        propertyBlock.SetColor(NewColorId, newColor);
        propertyBlock.SetFloat(ProgressId, progress);
        propertyBlock.SetFloat(FillModeId, fillMode);
        propertyBlock.SetFloat(MaxRadiusId, maxRadius);

        propertyBlock.SetVector(
            ImpactPointId,
            new Vector4(
                impactPoint.x,
                impactPoint.y,
                0f,
                0f));

        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    private float CalculateMaxCornerDistance(
        Vector2 impactPoint)
    {
        float maximumDistance = 0f;

        for (int i = 0; i < 6; i++)
        {
            float angle =
                i * 60f * Mathf.Deg2Rad;

            Vector2 corner = new Vector2(
                Mathf.Cos(angle) * hexRadius,
                Mathf.Sin(angle) * hexRadius);

            float distance =
                Vector2.Distance(impactPoint, corner);

            maximumDistance =
                Mathf.Max(maximumDistance, distance);
        }

        return maximumDistance;
    }

    private bool EnsureRenderer()
    {
        if (targetRenderer != null)
            return true;

        return BindRendererFromTile();
    }
}
