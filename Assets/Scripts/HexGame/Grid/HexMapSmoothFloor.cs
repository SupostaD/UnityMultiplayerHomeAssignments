using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HexMapSmoothFloor : MonoBehaviour
{
    [Header("Required Reference")]
    [SerializeField] private MeshCollider floorCollider;

    [Header("Shape")]
    [SerializeField, Min(0.01f)] private float floorThickness = 0.2f;

    [Header("Optional Preview")]
    [Tooltip("Assign only if you want the generated floor mesh to be visible.")]
    [SerializeField] private MeshFilter previewMeshFilter;

    private Mesh generatedMesh;

    public bool Rebuild(
        IReadOnlyList<HexTile> tiles,
        float tileOuterRadius,
        HexOrientation orientation)
    {
        if (floorCollider == null)
        {
            Debug.LogError(
                "HexMapSmoothFloor requires a MeshCollider reference.",
                this
            );
            return false;
        }

        if (tiles == null || tiles.Count == 0)
        {
            Debug.LogError(
                "HexMapSmoothFloor requires at least one generated tile.",
                this
            );
            return false;
        }

        List<Vector2> projectedCorners = CollectProjectedTileCorners(
            tiles,
            Mathf.Max(0.01f, tileOuterRadius),
            orientation,
            out float topY
        );

        List<Vector2> hull = BuildConvexHull(projectedCorners);

        if (hull.Count < 3)
        {
            Debug.LogError(
                "HexMapSmoothFloor could not build a valid map outline.",
                this
            );
            return false;
        }

        ReplaceGeneratedMesh(
            CreatePrismMesh(
                hull,
                topY,
                Mathf.Max(0.01f, floorThickness)
            )
        );

        floorCollider.sharedMesh = null;
        floorCollider.convex = true;
        floorCollider.sharedMesh = generatedMesh;
        floorCollider.enabled = true;

        if (previewMeshFilter != null)
            previewMeshFilter.sharedMesh = generatedMesh;

        return true;
    }

    public void Clear()
    {
        if (floorCollider != null)
            floorCollider.sharedMesh = null;

        if (previewMeshFilter != null)
            previewMeshFilter.sharedMesh = null;

        DestroyGeneratedMesh();
    }

    private void OnDestroy()
    {
        DestroyGeneratedMesh();
    }

    private List<Vector2> CollectProjectedTileCorners(
        IReadOnlyList<HexTile> tiles,
        float tileOuterRadius,
        HexOrientation orientation,
        out float topY)
    {
        List<Vector2> corners = new List<Vector2>(tiles.Count * 6);
        float totalTopY = 0f;
        int validTileCount = 0;
        float startingAngle =
            orientation == HexOrientation.FlatTop ? 0f : 30f;

        foreach (HexTile tile in tiles)
        {
            if (tile == null)
                continue;

            Vector3 localTopCenter =
                transform.InverseTransformPoint(tile.WorldTopCenter);

            totalTopY += localTopCenter.y;
            validTileCount++;

            for (int cornerIndex = 0; cornerIndex < 6; cornerIndex++)
            {
                float angleRadians =
                    (startingAngle + cornerIndex * 60f) * Mathf.Deg2Rad;

                Vector3 tileLocalCorner = new Vector3(
                    Mathf.Cos(angleRadians) * tileOuterRadius,
                    0f,
                    Mathf.Sin(angleRadians) * tileOuterRadius
                );

                Vector3 worldCorner =
                    tile.transform.TransformPoint(tileLocalCorner);
                Vector3 floorLocalCorner =
                    transform.InverseTransformPoint(worldCorner);

                corners.Add(
                    new Vector2(floorLocalCorner.x, floorLocalCorner.z)
                );
            }
        }

        topY = validTileCount > 0
            ? totalTopY / validTileCount
            : 0f;

        return corners;
    }

    private static List<Vector2> BuildConvexHull(List<Vector2> points)
    {
        if (points == null || points.Count < 3)
            return new List<Vector2>();

        points.Sort(ComparePoints);

        List<Vector2> hull = new List<Vector2>(points.Count * 2);

        foreach (Vector2 point in points)
        {
            while (hull.Count >= 2 &&
                   Cross(
                       hull[hull.Count - 2],
                       hull[hull.Count - 1],
                       point
                   ) <= 0.00001f)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        int lowerHullCount = hull.Count;

        for (int i = points.Count - 2; i >= 0; i--)
        {
            Vector2 point = points[i];

            while (hull.Count > lowerHullCount &&
                   Cross(
                       hull[hull.Count - 2],
                       hull[hull.Count - 1],
                       point
                   ) <= 0.00001f)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        if (hull.Count > 1)
            hull.RemoveAt(hull.Count - 1);

        return hull;
    }

    private static Mesh CreatePrismMesh(
        IReadOnlyList<Vector2> hull,
        float topY,
        float thickness)
    {
        int sideCount = hull.Count;
        Vector3[] vertices = new Vector3[sideCount * 2];
        List<int> triangles = new List<int>((sideCount - 2) * 6 + sideCount * 6);
        float bottomY = topY - thickness;

        for (int i = 0; i < sideCount; i++)
        {
            Vector2 point = hull[i];
            vertices[i] = new Vector3(point.x, topY, point.y);
            vertices[i + sideCount] = new Vector3(
                point.x,
                bottomY,
                point.y
            );
        }

        for (int i = 1; i < sideCount - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i + 1);
            triangles.Add(i);

            triangles.Add(sideCount);
            triangles.Add(sideCount + i);
            triangles.Add(sideCount + i + 1);
        }

        for (int i = 0; i < sideCount; i++)
        {
            int next = (i + 1) % sideCount;
            int bottomCurrent = i + sideCount;
            int bottomNext = next + sideCount;

            triangles.Add(i);
            triangles.Add(next);
            triangles.Add(bottomNext);

            triangles.Add(i);
            triangles.Add(bottomNext);
            triangles.Add(bottomCurrent);
        }

        Mesh mesh = new Mesh
        {
            name = "Generated Smooth Hex Map Floor"
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void ReplaceGeneratedMesh(Mesh replacement)
    {
        DestroyGeneratedMesh();
        generatedMesh = replacement;
    }

    private void DestroyGeneratedMesh()
    {
        if (generatedMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(generatedMesh);
        else
            DestroyImmediate(generatedMesh);

        generatedMesh = null;
    }

    private static int ComparePoints(Vector2 left, Vector2 right)
    {
        int xComparison = left.x.CompareTo(right.x);

        return xComparison != 0
            ? xComparison
            : left.y.CompareTo(right.y);
    }

    private static float Cross(Vector2 origin, Vector2 a, Vector2 b)
    {
        return
            (a.x - origin.x) * (b.y - origin.y) -
            (a.y - origin.y) * (b.x - origin.x);
    }
}
