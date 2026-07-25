#ifndef HEX_CAPTURE_DISTANCE_INCLUDED
#define HEX_CAPTURE_DISTANCE_INCLUDED

void HexCaptureDistance_float(
    float2 Position,
    float2 ImpactPoint,
    float FillMode,
    float HexRadius,
    out float Distance)
{
    if (FillMode < 0.5)
    {
        Distance = distance(Position, ImpactPoint);
        return;
    }

    float radius = max(HexRadius, 0.0001);
    float halfRadius = radius * 0.5;
    float cornerHeight = radius * 0.86602540378;

    float minimumDistance = distance(Position, float2(radius, 0.0));
    minimumDistance = min(
        minimumDistance,
        distance(Position, float2(halfRadius, cornerHeight))
    );
    minimumDistance = min(
        minimumDistance,
        distance(Position, float2(-halfRadius, cornerHeight))
    );
    minimumDistance = min(
        minimumDistance,
        distance(Position, float2(-radius, 0.0))
    );
    minimumDistance = min(
        minimumDistance,
        distance(Position, float2(-halfRadius, -cornerHeight))
    );
    minimumDistance = min(
        minimumDistance,
        distance(Position, float2(halfRadius, -cornerHeight))
    );

    Distance = minimumDistance;
}

void HexCaptureDistance_half(
    half2 Position,
    half2 ImpactPoint,
    half FillMode,
    half HexRadius,
    out half Distance)
{
    if (FillMode < 0.5h)
    {
        Distance = distance(Position, ImpactPoint);
        return;
    }

    half radius = max(HexRadius, 0.0001h);
    half halfRadius = radius * 0.5h;
    half cornerHeight = radius * 0.8660254h;

    half minimumDistance = distance(Position, half2(radius, 0.0h));
    minimumDistance = min(
        minimumDistance,
        distance(Position, half2(halfRadius, cornerHeight))
    );
    minimumDistance = min(
        minimumDistance,
        distance(Position, half2(-halfRadius, cornerHeight))
    );
    minimumDistance = min(
        minimumDistance,
        distance(Position, half2(-radius, 0.0h))
    );
    minimumDistance = min(
        minimumDistance,
        distance(Position, half2(-halfRadius, -cornerHeight))
    );
    minimumDistance = min(
        minimumDistance,
        distance(Position, half2(halfRadius, -cornerHeight))
    );

    Distance = minimumDistance;
}

#endif
