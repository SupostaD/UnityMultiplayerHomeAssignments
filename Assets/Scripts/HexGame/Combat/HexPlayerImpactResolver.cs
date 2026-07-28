using UnityEngine;

public static class HexPlayerImpactResolver
{
    public static bool TryResolve(
        HexPlayerCollisionSettings settings,
        Vector3 firstVelocity,
        Vector3 secondVelocity,
        Vector3 firstToSecondNormal,
        float firstMass,
        float secondMass,
        bool firstIsDashing,
        bool secondIsDashing,
        out Vector3 firstResolvedPlanarVelocity,
        out Vector3 secondResolvedPlanarVelocity,
        out float verticalLift)
    {
        firstResolvedPlanarVelocity = Vector3.zero;
        secondResolvedPlanarVelocity = Vector3.zero;
        verticalLift = 0f;

        if (settings == null)
            return false;

        firstVelocity.y = 0f;
        secondVelocity.y = 0f;
        firstToSecondNormal.y = 0f;

        if (firstToSecondNormal.sqrMagnitude <= 0.0001f)
        {
            firstToSecondNormal =
                secondVelocity - firstVelocity;
        }

        if (firstToSecondNormal.sqrMagnitude <= 0.0001f)
            return false;

        firstToSecondNormal.Normalize();

        float closingSpeed = Vector3.Dot(
            firstVelocity - secondVelocity,
            firstToSecondNormal
        );

        if (closingSpeed < settings.MinimumImpactSpeed)
            return false;

        firstMass = Mathf.Max(0.01f, firstMass);
        secondMass = Mathf.Max(0.01f, secondMass);
        float firstInverseMass = 1f / firstMass;
        float secondInverseMass = 1f / secondMass;
        float dashMultiplier =
            firstIsDashing || secondIsDashing
                ? settings.DashImpactMultiplier
                : 1f;
        float impulseMagnitude =
            (1f + settings.Restitution) *
            closingSpeed *
            dashMultiplier /
            (firstInverseMass + secondInverseMass);
        Vector3 firstVelocityChange =
            -firstToSecondNormal *
            impulseMagnitude *
            firstInverseMass;
        Vector3 secondVelocityChange =
            firstToSecondNormal *
            impulseMagnitude *
            secondInverseMass;

        firstVelocityChange = Vector3.ClampMagnitude(
            firstVelocityChange,
            settings.MaximumVelocityChange
        );
        secondVelocityChange = Vector3.ClampMagnitude(
            secondVelocityChange,
            settings.MaximumVelocityChange
        );

        verticalLift = Mathf.Min(
            settings.MaximumVerticalLift,
            closingSpeed *
            settings.VerticalLiftPerImpactSpeed
        );
        firstResolvedPlanarVelocity =
            firstVelocity +
            firstVelocityChange;
        secondResolvedPlanarVelocity =
            secondVelocity +
            secondVelocityChange;
        return true;
    }
}
