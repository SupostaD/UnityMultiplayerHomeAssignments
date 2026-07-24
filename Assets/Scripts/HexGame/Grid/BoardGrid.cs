using System.Collections.Generic;
using UnityEngine;

public class BoardGrid : MonoBehaviour
{
    private readonly Dictionary<HexCoord, HexTile> tiles = new Dictionary<HexCoord, HexTile>();

    public int Count => tiles.Count;
    public IReadOnlyDictionary<HexCoord, HexTile> Tiles => tiles;

    public bool IsOccupied(HexCoord coord)
    {
        return tiles.ContainsKey(coord);
    }

    public bool TryAddTile(HexCoord coord, HexTile tile)
    {
        if (tile == null)
        {
            Debug.LogError($"Cannot register a null tile at {coord}.", this);
            return false;
        }

        if (tiles.ContainsKey(coord))
        {
            Debug.LogError($"A tile is already registered at {coord}.", this);
            return false;
        }

        tiles.Add(coord, tile);
        return true;
    }

    public bool TryGetTile(HexCoord coord, out HexTile tile)
    {
        return tiles.TryGetValue(coord, out tile);
    }

    public HexTile GetTile(HexCoord coord)
    {
        tiles.TryGetValue(coord, out HexTile tile);
        return tile;
    }

    public bool RemoveTile(HexCoord coord)
    {
        return tiles.Remove(coord);
    }

    public void Clear()
    {
        tiles.Clear();
    }
}
