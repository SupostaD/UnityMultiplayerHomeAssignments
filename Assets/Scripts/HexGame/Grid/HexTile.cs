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

    [SerializeField] private Renderer tileRenderer;
    [SerializeField] private Collider tileCollider;
    [SerializeField] private HexCoord coordinate;

    private MaterialPropertyBlock propertyBlock;

    public HexCoord Coordinate => coordinate;
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

    private void OnEnable()
    {
        ActiveTiles.Add(this);
    }

    private void OnDisable()
    {
        ActiveTiles.Remove(this);
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
        if (tileRenderer == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        tileRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        tileRenderer.SetPropertyBlock(propertyBlock);
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
}
