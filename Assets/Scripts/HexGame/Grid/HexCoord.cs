using System;
using UnityEngine;

[Serializable]
public struct HexCoord : IEquatable<HexCoord>
{
    public int q;
    public int r;
    public int layer;

    public int S => -q - r;

    public HexCoord(int q, int r, int layer = 0)
    {
        this.q = q;
        this.r = r;
        this.layer = layer;
    }

    public HexCoord GetNeighbor(int direction)
    {
        int normalizedDirection = ((direction % 6) + 6) % 6;
        HexCoord directionOffset = GetDirection(normalizedDirection);

        return new HexCoord(
            q + directionOffset.q,
            r + directionOffset.r,
            layer
        );
    }

    public int DistanceTo(HexCoord other)
    {
        int qDistance = Mathf.Abs(q - other.q);
        int rDistance = Mathf.Abs(r - other.r);
        int sDistance = Mathf.Abs(S - other.S);
        int layerDistance = Mathf.Abs(layer - other.layer);

        return Mathf.Max(
            layerDistance,
            Mathf.Max(qDistance, Mathf.Max(rDistance, sDistance))
        );
    }

    public bool Equals(HexCoord other)
    {
        return q == other.q &&
               r == other.r &&
               layer == other.layer;
    }

    public override bool Equals(object obj)
    {
        return obj is HexCoord other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = q;
            hashCode = (hashCode * 397) ^ r;
            hashCode = (hashCode * 397) ^ layer;
            return hashCode;
        }
    }

    public override string ToString()
    {
        return $"({q}, {r}, layer {layer})";
    }

    public static HexCoord Round(float fractionalQ, float fractionalR)
    {
        float fractionalS = -fractionalQ - fractionalR;

        int roundedQ = Mathf.RoundToInt(fractionalQ);
        int roundedR = Mathf.RoundToInt(fractionalR);
        int roundedS = Mathf.RoundToInt(fractionalS);

        float qDifference = Mathf.Abs(roundedQ - fractionalQ);
        float rDifference = Mathf.Abs(roundedR - fractionalR);
        float sDifference = Mathf.Abs(roundedS - fractionalS);

        if (qDifference > rDifference && qDifference > sDifference)
            roundedQ = -roundedR - roundedS;
        else if (rDifference > sDifference)
            roundedR = -roundedQ - roundedS;

        return new HexCoord(roundedQ, roundedR, 0);
    }

    public static HexCoord GetDirection(int direction)
    {
        switch (direction)
        {
            case 0:
                return new HexCoord(1, 0);
            case 1:
                return new HexCoord(1, -1);
            case 2:
                return new HexCoord(0, -1);
            case 3:
                return new HexCoord(-1, 0);
            case 4:
                return new HexCoord(-1, 1);
            default:
                return new HexCoord(0, 1);
        }
    }

    public static HexCoord operator +(HexCoord left, HexCoord right)
    {
        return new HexCoord(
            left.q + right.q,
            left.r + right.r,
            left.layer + right.layer
        );
    }

    public static HexCoord operator -(HexCoord left, HexCoord right)
    {
        return new HexCoord(
            left.q - right.q,
            left.r - right.r,
            left.layer - right.layer
        );
    }

    public static bool operator ==(HexCoord left, HexCoord right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(HexCoord left, HexCoord right)
    {
        return !left.Equals(right);
    }
}
