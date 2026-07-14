using UnityEngine;

[CreateAssetMenu(menuName = "Race/Movement Settings", fileName = "RaceMovementSettings")]
public class RaceMovementSettings : ScriptableObject
{
    [Header("Speed")]
    [Min(0f)] public float maxForwardSpeed = 18f;
    [Min(0f)] public float maxReverseSpeed = 7f;
    [Min(0f)] public float acceleration = 28f;
    [Min(0f)] public float brakeDeceleration = 36f;
    [Min(0f)] public float coastDeceleration = 14f;

    [Header("Steering")]
    [Min(0f)] public float turnSpeed = 135f;
    [Range(0f, 1f)] public float minTurnSpeedFactor = 0.25f;
    [Min(0f)] public float handbrakeTurnMultiplier = 1.55f;
    [Min(0f)] public float handbrakeDeceleration = 22f;

    [Header("Track")]
    [Min(0f)] public float offTrackSpeedMultiplier = 0.65f;

    [Header("Collision")]
    public LayerMask collisionMask = ~0;
    [Min(0f)] public float collisionSkinWidth = 0.05f;
    [Min(1)] public int collisionSlideIterations = 2;
    [Range(0f, 1f)] public float walkableNormalThreshold = 0.65f;

    [Header("Ground Check")]
    public LayerMask groundMask = ~0;
    [Min(0f)] public float groundCheckHeight = 0.75f;
    [Min(0f)] public float groundCheckDistance = 1.5f;
    [Min(0f)] public float groundHeightOffset = 0f;
    [Min(0f)] public float groundSnapSpeed = 20f;
    [Min(0f)] public float gravity = 25f;
    [Range(0f, 1f)] public float airControlMultiplier = 0.35f;
    public bool alignToGround = true;
    [Min(0f)] public float groundAlignSpeed = 12f;
}
