using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DefaultExecutionOrder(-1000)]
public class HexTile : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly HashSet<HexTile> ActiveTiles =
        new HashSet<HexTile>();
    private static readonly Dictionary<Transform, HexTile> TilesByTransform =
        new Dictionary<Transform, HexTile>();

    [SerializeField] private Renderer tileRenderer;
    [SerializeField] private Collider tileCollider;
    [SerializeField] private HexCoord coordinate;

    private MaterialPropertyBlock propertyBlock;
    private HexCaptureVisual captureVisual;

    public HexCoord Coordinate => coordinate;
    public Renderer TileRenderer => tileRenderer;
    public Collider TileCollider => tileCollider;
    public Vector3 WorldTopCenter
    {
        get
        {
            if (tileRenderer == null)
                return transform.position;

            Bounds bounds = tileRenderer.bounds;
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
    }

    private void Awake()
    {
        RegisterTile();
    }

    private void OnEnable()
    {
        RegisterTile();
    }

    private void OnDisable()
    {
        ActiveTiles.Remove(this);

        if (TilesByTransform.TryGetValue(transform, out HexTile tile) &&
            tile == this)
        {
            TilesByTransform.Remove(transform);
        }
    }

    public static bool TryGetForTransform(
        Transform tileTransform,
        out HexTile tile)
    {
        if (tileTransform == null)
        {
            tile = null;
            return false;
        }

        return TilesByTransform.TryGetValue(tileTransform, out tile) &&
               tile != null;
    }

    public static void CollectUnderRoot(
        Transform root,
        List<HexTile> destination)
    {
        destination.Clear();

        if (root == null)
            return;

        foreach (HexTile tile in ActiveTiles)
        {
            if (tile == null)
                continue;

            Transform tileTransform = tile.transform;

            if (tileTransform == root ||
                tileTransform.IsChildOf(root))
            {
                destination.Add(tile);
            }
        }
    }

    public void Initialize(HexCoord newCoordinate)
    {
        coordinate = newCoordinate;
        gameObject.name =
            $"Hex {coordinate.q}, {coordinate.r}, L{coordinate.layer}";
    }

    public void SetCoordinate(HexCoord newCoordinate)
    {
        coordinate = newCoordinate;
    }

    public void SetColor(Color color)
    {
        if (captureVisual != null)
        {
            captureVisual.SetColorImmediate(color);
            return;
        }

        if (tileRenderer == null)
        {
            Debug.LogError("HexTile: Tile Renderer is not assigned.", this);
            return;
        }

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        tileRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        tileRenderer.SetPropertyBlock(propertyBlock);
    }

    public Vector2 WorldToCapturePoint(Vector3 worldPosition)
    {
        if (tileRenderer == null)
        {
            Debug.LogError("HexTile: Tile Renderer is not assigned.", this);
            return Vector2.zero;
        }

        Vector3 localPosition =
            tileRenderer.transform.InverseTransformPoint(worldPosition);

        return new Vector2(localPosition.x, localPosition.z);
    }

    public void CaptureFromPlayer(
        Color color,
        Vector2 localImpactPoint)
    {
        if (captureVisual == null)
        {
            Debug.LogError(
                "HexTile: Hex Capture Visual is not available on this tile.",
                this
            );
            return;
        }

        captureVisual.CaptureFromPlayer(color, localImpactPoint);
    }

    public void CaptureFromCorners(Color color)
    {
        if (captureVisual == null)
        {
            Debug.LogError(
                "HexTile: Hex Capture Visual is not available on this tile.",
                this
            );
            return;
        }

        captureVisual.CaptureFromCorners(color);
    }

    public void BindCaptureVisual(HexCaptureVisual visual)
    {
        if (visual == null)
        {
            Debug.LogError(
                "HexTile: Cannot bind a missing Hex Capture Visual.",
                this
            );
            return;
        }

        captureVisual = visual;
    }

    public void ConfigureCaptureVisual(
        HexTerritoryCaptureSettings settings)
    {
        if (captureVisual == null)
        {
            Debug.LogError(
                "HexTile: Hex Capture Visual is not available on this tile.",
                this
            );
            return;
        }

        captureVisual.Configure(settings);
    }

    public void SetCollisionEnabled(bool isEnabled)
    {
        if (tileCollider == null)
        {
            Debug.LogError("HexTile: Tile Collider is not assigned.", this);
            return;
        }

        tileCollider.enabled = isEnabled;
    }

    private void RegisterTile()
    {
        ActiveTiles.Add(this);
        TilesByTransform[transform] = this;
    }
}
