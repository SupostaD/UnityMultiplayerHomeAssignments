using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum HexMapShape
{
    Hexagon = 0,
    Rectangle = 1
}

public enum HexOrientation
{
    PointyTop = 0,
    FlatTop = 1
}

[DefaultExecutionOrder(1000)]
public class HexMapGenerator : MonoBehaviour
{
    public const int HexagonCornerCount = 6;

    [Header("Required References")]
    [SerializeField] private BoardGrid boardGrid;
    [SerializeField] private HexTile tilePrefab;
    [FormerlySerializedAs("tilesRoot")]
    [SerializeField] private Transform baseTilesRoot;
    [SerializeField] private Transform authoredTilesRoot;

    [Header("Base Map Generation")]
    [FormerlySerializedAs("generateOnAwake")]
    [Tooltip("At runtime the base map is generated only when Base Tiles Root contains no saved tiles.")]
    [SerializeField] private bool generateAtRuntimeWhenBaseIsEmpty = true;
    [SerializeField] private HexMapShape mapShape = HexMapShape.Hexagon;
    [SerializeField] private HexOrientation orientation = HexOrientation.FlatTop;
    [SerializeField, Min(0)] private int hexagonRadius = 7;
    [SerializeField, Min(1)] private int rectangleWidth = 12;
    [SerializeField, Min(1)] private int rectangleHeight = 10;
    [SerializeField, Min(0.01f)] private float tileOuterRadius = 2f;
    [SerializeField, Min(0f)] private float tileGap;
    [SerializeField] private bool centerMapOnGenerator = true;

    [Header("Multi-Level Authoring")]
    [SerializeField, Min(0.01f)] private float layerHeight = 1f;

    [Header("Smooth Physics Floor")]
    [Tooltip("Only the generated base layer uses this shared collider. Authored upper tiles keep their own colliders.")]
    [SerializeField] private HexMapSmoothFloor smoothPhysicsFloor;

    [SerializeField, HideInInspector]
    private List<HexTile> generatedBaseTiles = new List<HexTile>();
    [SerializeField, HideInInspector]
    private List<HexTile> authoredTiles = new List<HexTile>();
    [SerializeField, HideInInspector]
    private Vector3 centerOffset;

    private readonly List<HexTile> registeredTiles = new List<HexTile>();
    private readonly List<HexCoord> registeredCoordinates =
        new List<HexCoord>();
    private readonly Dictionary<HexCoord, int> coordinateIndices =
        new Dictionary<HexCoord, int>();
    private int duplicateCoordinateCount;

    public BoardGrid Board => boardGrid;
    public int GeneratedTileCount => registeredCoordinates.Count;
    public int BaseGeneratedTileCount => generatedBaseTiles.Count;
    public int AuthoredTileCount => authoredTiles.Count;
    public int DuplicateCoordinateCount => duplicateCoordinateCount;
    public float TileOuterRadius => tileOuterRadius;
    public float LayerHeight => Mathf.Max(0.01f, layerHeight);
    public HexOrientation Orientation => orientation;
    public bool IsHexagonMap => mapShape == HexMapShape.Hexagon;
    public Vector3 MapCenterWorldPosition =>
        CoordinateToWorldPosition(new HexCoord(0, 0, 0));

    private Transform BaseTileRoot =>
        baseTilesRoot != null ? baseTilesRoot : transform;

    private Transform AuthoredTileRoot =>
        authoredTilesRoot;

    private float CellRadius =>
        Mathf.Max(0.01f, tileOuterRadius + tileGap);

    private void OnEnable()
    {
        if (!Application.isPlaying)
            RebuildMapIndex();
    }

    private void Awake()
    {
        RebuildMapIndex();

        if (generatedBaseTiles.Count == 0 &&
            generateAtRuntimeWhenBaseIsEmpty)
        {
            Generate();
        }
        else
        {
            BuildSmoothPhysicsFloor();
        }
    }

    public void Generate()
    {
        if (!ValidateReferences())
            return;

        ClearGeneratedTiles();

        List<HexCoord> coordinates = BuildCoordinates();
        centerOffset = centerMapOnGenerator
            ? CalculateCenterOffset(coordinates)
            : Vector3.zero;

        foreach (HexCoord coordinate in coordinates)
        {
            HexTile tile = Instantiate(tilePrefab, BaseTileRoot);
            Transform tileTransform = tile.transform;

            tileTransform.localPosition =
                CoordinateToBaseLocalPosition(coordinate) -
                centerOffset;
            tileTransform.localRotation = Quaternion.identity;
            tile.Initialize(coordinate);
            generatedBaseTiles.Add(tile);
        }

        RebuildMapIndex();
        BuildSmoothPhysicsFloor();
    }

    public void ClearGeneratedTiles()
    {
        RefreshTileListsFromRoots();
        DestroyTiles(generatedBaseTiles);
        generatedBaseTiles.Clear();
        RebuildMapIndex();

        if (smoothPhysicsFloor != null)
            smoothPhysicsFloor.Clear();
    }

    public void RebuildMapIndex()
    {
        if (generatedBaseTiles == null)
            generatedBaseTiles = new List<HexTile>();

        if (authoredTiles == null)
            authoredTiles = new List<HexTile>();

        RefreshTileListsFromRoots();
        registeredTiles.Clear();
        registeredCoordinates.Clear();
        coordinateIndices.Clear();
        duplicateCoordinateCount = 0;

        if (boardGrid != null)
            boardGrid.Clear();

        RegisterTiles(generatedBaseTiles);
        RegisterTiles(authoredTiles);
    }

    public Vector3 CoordinateToWorldPosition(HexCoord coordinate)
    {
        if (TryGetTile(coordinate, out HexTile registeredTile))
            return registeredTile.transform.position;

        Vector3 localPosition =
            CoordinateToBaseLocalPosition(coordinate) -
            centerOffset +
            Vector3.up * (coordinate.layer * LayerHeight);

        return BaseTileRoot.TransformPoint(localPosition);
    }

    public HexCoord WorldPositionToAuthoredCoordinate(
        Vector3 worldPosition)
    {
        Vector3 mapLocalPosition =
            BaseTileRoot.InverseTransformPoint(worldPosition) +
            centerOffset;
        float size = CellRadius;
        float fractionalQ;
        float fractionalR;

        if (orientation == HexOrientation.PointyTop)
        {
            fractionalR =
                (2f / 3f) * mapLocalPosition.z / size;
            fractionalQ =
                mapLocalPosition.x /
                (Mathf.Sqrt(3f) * size) -
                fractionalR * 0.5f;
        }
        else
        {
            fractionalQ =
                (2f / 3f) * mapLocalPosition.x / size;
            fractionalR =
                mapLocalPosition.z /
                (Mathf.Sqrt(3f) * size) -
                fractionalQ * 0.5f;
        }

        HexCoord coordinate =
            HexCoord.Round(fractionalQ, fractionalR);
        coordinate.layer = Mathf.Max(
            0,
            Mathf.RoundToInt(
                mapLocalPosition.y / LayerHeight
            )
        );
        return coordinate;
    }

    public bool TryWorldToCoordinate(
        Vector3 worldPosition,
        out HexCoord coordinate)
    {
        coordinate = default;

        if (registeredTiles.Count == 0)
            RebuildMapIndex();

        float maximumPlanarDistance =
            Mathf.Max(0.01f, CellRadius * 1.05f);
        float maximumPlanarDistanceSquared =
            maximumPlanarDistance * maximumPlanarDistance;
        float bestScore = float.PositiveInfinity;
        int bestIndex = -1;

        for (int i = 0; i < registeredTiles.Count; i++)
        {
            HexTile tile = registeredTiles[i];

            if (tile == null)
                continue;

            Vector3 tileTop = tile.WorldTopCenter;
            Vector2 planarDifference = new Vector2(
                worldPosition.x - tileTop.x,
                worldPosition.z - tileTop.z
            );
            float planarDistanceSquared =
                planarDifference.sqrMagnitude;

            if (planarDistanceSquared > maximumPlanarDistanceSquared)
                continue;

            float verticalDistance =
                Mathf.Abs(worldPosition.y - tileTop.y);
            float score =
                planarDistanceSquared +
                verticalDistance * verticalDistance * 4f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        if (bestIndex < 0)
            return false;

        coordinate = registeredCoordinates[bestIndex];
        return true;
    }

    public bool TryGetTileAtWorldPosition(
        Vector3 worldPosition,
        out HexTile tile)
    {
        tile = null;

        return TryWorldToCoordinate(
                   worldPosition,
                   out HexCoord coordinate) &&
               TryGetTile(coordinate, out tile);
    }

    public bool TryGetTile(
        HexCoord coordinate,
        out HexTile tile)
    {
        tile = null;

        return coordinateIndices.TryGetValue(
                   coordinate,
                   out int index) &&
               index >= 0 &&
               index < registeredTiles.Count &&
               (tile = registeredTiles[index]) != null;
    }

    public bool TryGetCoordinateIndex(
        HexCoord coordinate,
        out int index)
    {
        return coordinateIndices.TryGetValue(coordinate, out index);
    }

    public bool TryGetCoordinateAtIndex(
        int index,
        out HexCoord coordinate)
    {
        if (index < 0 || index >= registeredCoordinates.Count)
        {
            coordinate = default;
            return false;
        }

        coordinate = registeredCoordinates[index];
        return true;
    }

    public bool TryGetTileAtIndex(int index, out HexTile tile)
    {
        if (index < 0 || index >= registeredTiles.Count)
        {
            tile = null;
            return false;
        }

        tile = registeredTiles[index];
        return tile != null;
    }

    public bool TryGetHexagonCornerTile(
        int cornerIndex,
        out HexCoord coordinate,
        out HexTile tile)
    {
        coordinate = default;
        tile = null;

        if (!IsHexagonMap)
            return false;

        int radius = Mathf.Max(0, hexagonRadius);

        if (radius == 0)
            return false;

        int normalizedCorner =
            ((cornerIndex % HexagonCornerCount) +
             HexagonCornerCount) %
            HexagonCornerCount;

        switch (normalizedCorner)
        {
            case 0:
                coordinate = new HexCoord(radius, 0, 0);
                break;
            case 1:
                coordinate = new HexCoord(radius, -radius, 0);
                break;
            case 2:
                coordinate = new HexCoord(0, -radius, 0);
                break;
            case 3:
                coordinate = new HexCoord(-radius, 0, 0);
                break;
            case 4:
                coordinate = new HexCoord(-radius, radius, 0);
                break;
            default:
                coordinate = new HexCoord(0, radius, 0);
                break;
        }

        return TryGetTile(coordinate, out tile);
    }

    public bool TryGetHexagonCornerWorldPosition(
        int cornerIndex,
        out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        if (!TryGetHexagonCornerTile(
                cornerIndex,
                out _,
                out HexTile tile))
        {
            return false;
        }

        worldPosition = tile.WorldTopCenter;
        return true;
    }

    private bool ValidateReferences()
    {
        if (boardGrid == null)
        {
            Debug.LogError(
                "HexMapGenerator requires a BoardGrid reference.",
                this
            );
            return false;
        }

        if (tilePrefab == null)
        {
            Debug.LogError(
                "HexMapGenerator requires a HexTile prefab.",
                this
            );
            return false;
        }

        return true;
    }

    private void RefreshTileListsFromRoots()
    {
        HexTile.CollectUnderRoot(
            BaseTileRoot,
            generatedBaseTiles
        );

        if (AuthoredTileRoot != null)
        {
            generatedBaseTiles.RemoveAll(
                tile =>
                    tile != null &&
                    (tile.transform == AuthoredTileRoot ||
                     tile.transform.IsChildOf(AuthoredTileRoot))
            );

            HexTile.CollectUnderRoot(
                AuthoredTileRoot,
                authoredTiles
            );
        }
        else
        {
            authoredTiles.Clear();
        }

        for (int i = 0; i < authoredTiles.Count; i++)
        {
            HexTile tile = authoredTiles[i];

            if (tile == null)
                continue;

            HexCoord coordinate =
                WorldPositionToAuthoredCoordinate(
                    tile.transform.position
                );

            if (tile.Coordinate != coordinate)
                tile.SetCoordinate(coordinate);
        }

        generatedBaseTiles.Sort(CompareTiles);
        authoredTiles.Sort(CompareTiles);
    }

    private static int CompareTiles(
        HexTile left,
        HexTile right)
    {
        if (left == right)
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        HexCoord leftCoordinate = left.Coordinate;
        HexCoord rightCoordinate = right.Coordinate;
        int comparison =
            leftCoordinate.layer.CompareTo(
                rightCoordinate.layer
            );

        if (comparison != 0)
            return comparison;

        comparison =
            leftCoordinate.q.CompareTo(rightCoordinate.q);

        if (comparison != 0)
            return comparison;

        comparison =
            leftCoordinate.r.CompareTo(rightCoordinate.r);

        if (comparison != 0)
            return comparison;

        comparison =
            left.transform.position.x.CompareTo(
                right.transform.position.x
            );

        if (comparison != 0)
            return comparison;

        comparison =
            left.transform.position.y.CompareTo(
                right.transform.position.y
            );

        if (comparison != 0)
            return comparison;

        return left.transform.position.z.CompareTo(
            right.transform.position.z
        );
    }

    private void RegisterTiles(IReadOnlyList<HexTile> tiles)
    {
        if (tiles == null)
            return;

        for (int i = 0; i < tiles.Count; i++)
        {
            HexTile tile = tiles[i];

            if (tile == null)
                continue;

            HexCoord coordinate = tile.Coordinate;

            if (coordinateIndices.ContainsKey(coordinate))
            {
                duplicateCoordinateCount++;
                Debug.LogError(
                    $"Duplicate map coordinate {coordinate} on {tile.name}.",
                    tile
                );
                continue;
            }

            int index = registeredCoordinates.Count;
            coordinateIndices.Add(coordinate, index);
            registeredCoordinates.Add(coordinate);
            registeredTiles.Add(tile);

            if (boardGrid != null)
                boardGrid.TryAddTile(coordinate, tile);
        }
    }

    private List<HexCoord> BuildCoordinates()
    {
        return mapShape == HexMapShape.Hexagon
            ? BuildHexagonCoordinates()
            : BuildRectangleCoordinates();
    }

    private List<HexCoord> BuildHexagonCoordinates()
    {
        int radius = Mathf.Max(0, hexagonRadius);
        int diameter = radius * 2 + 1;
        List<HexCoord> coordinates =
            new List<HexCoord>(diameter * diameter);

        for (int q = -radius; q <= radius; q++)
        {
            int minimumR =
                Mathf.Max(-radius, -q - radius);
            int maximumR =
                Mathf.Min(radius, -q + radius);

            for (int r = minimumR; r <= maximumR; r++)
                coordinates.Add(new HexCoord(q, r, 0));
        }

        return coordinates;
    }

    private List<HexCoord> BuildRectangleCoordinates()
    {
        int width = Mathf.Max(1, rectangleWidth);
        int height = Mathf.Max(1, rectangleHeight);
        int startQ = -(width / 2);
        int startR = -(height / 2);
        List<HexCoord> coordinates =
            new List<HexCoord>(width * height);

        for (int qIndex = 0; qIndex < width; qIndex++)
        {
            for (int rIndex = 0; rIndex < height; rIndex++)
            {
                coordinates.Add(
                    new HexCoord(
                        startQ + qIndex,
                        startR + rIndex,
                        0
                    )
                );
            }
        }

        return coordinates;
    }

    private Vector3 CalculateCenterOffset(
        IReadOnlyList<HexCoord> coordinates)
    {
        if (coordinates == null || coordinates.Count == 0)
            return Vector3.zero;

        Vector3 firstPosition =
            CoordinateToBaseLocalPosition(coordinates[0]);
        Vector3 minimum = firstPosition;
        Vector3 maximum = firstPosition;

        for (int i = 1; i < coordinates.Count; i++)
        {
            Vector3 position =
                CoordinateToBaseLocalPosition(coordinates[i]);
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }

        return (minimum + maximum) * 0.5f;
    }

    private Vector3 CoordinateToBaseLocalPosition(
        HexCoord coordinate)
    {
        float size = CellRadius;
        float x;
        float z;

        if (orientation == HexOrientation.PointyTop)
        {
            x =
                size *
                Mathf.Sqrt(3f) *
                (coordinate.q + coordinate.r * 0.5f);
            z = size * 1.5f * coordinate.r;
        }
        else
        {
            x = size * 1.5f * coordinate.q;
            z =
                size *
                Mathf.Sqrt(3f) *
                (coordinate.r + coordinate.q * 0.5f);
        }

        return new Vector3(x, 0f, z);
    }

    private void BuildSmoothPhysicsFloor()
    {
        if (!Application.isPlaying)
        {
            if (smoothPhysicsFloor != null)
                smoothPhysicsFloor.Clear();

            foreach (HexTile tile in generatedBaseTiles)
            {
                if (tile != null)
                    tile.SetCollisionEnabled(true);
            }

            return;
        }

        if (smoothPhysicsFloor == null)
            return;

        if (!smoothPhysicsFloor.Rebuild(
                generatedBaseTiles,
                tileOuterRadius,
                orientation))
        {
            Debug.LogError(
                "Smooth physics floor could not be generated. Tile colliders remain enabled.",
                this
            );
            return;
        }

        foreach (HexTile tile in generatedBaseTiles)
        {
            if (tile != null)
                tile.SetCollisionEnabled(false);
        }
    }

    private static void DestroyTiles(IReadOnlyList<HexTile> tiles)
    {
        if (tiles == null)
            return;

        for (int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i] != null)
                DestroyTile(tiles[i]);
        }
    }

    private static void DestroyTile(HexTile tile)
    {
        if (tile == null)
            return;

        tile.gameObject.SetActive(false);

        if (Application.isPlaying)
            Destroy(tile.gameObject);
        else
            DestroyImmediate(tile.gameObject);
    }
}
