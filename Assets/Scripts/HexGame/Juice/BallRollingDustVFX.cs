using UnityEngine;

public class BallRollingDustVFX : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HexBallPlayerController controller;
    [SerializeField] private ParticleSystem rollingDust;
    [SerializeField] private Transform dustTransform;

    [Header("Settings")]
    [SerializeField, Min(0.01f)] private float minimumSpeedToEmit = 0.15f;
    [SerializeField] private bool requireGrounded = false;
    [SerializeField, Min(0f)] private float groundOffset = 0.05f;
    [SerializeField, Min(0f)] private float dustRate = 20f;

    private Vector3 previousPosition;
    private bool initialized;

    private void Awake()
    {
        previousPosition = transform.position;

        if (rollingDust != null && dustTransform == null)
            dustTransform = rollingDust.transform;

        SetDustRate(0f);

        if (rollingDust != null && !rollingDust.isPlaying)
            rollingDust.Play();
    }

    private void LateUpdate()
    {
        if (controller == null || rollingDust == null)
            return;

        if (dustTransform == null)
            dustTransform = rollingDust.transform;

        UpdateDustPosition();

        if (!initialized)
        {
            initialized = true;
            previousPosition = transform.position;
            return;
        }

        if (controller.IsEliminated)
        {
            SetDustRate(0f);
            previousPosition = transform.position;
            return;
        }

        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0f)
            return;

        Vector3 currentPosition = transform.position;
        Vector3 movement = currentPosition - previousPosition;
        movement.y = 0f;

        float speed = movement.magnitude / deltaTime;

        bool grounded = !requireGrounded || controller.IsGroundedForVFX();
        bool shouldEmit = grounded && speed >= minimumSpeedToEmit;

        SetDustRate(shouldEmit ? dustRate : 0f);

        previousPosition = currentPosition;
    }

    private void UpdateDustPosition()
    {
        Vector3 ballCenter = transform.position;

        float radius = controller != null
            ? controller.CollisionRadius
            : 0.5f;

        dustTransform.position = ballCenter - Vector3.up * (radius - groundOffset);
        dustTransform.rotation = Quaternion.identity;
    }

    private void SetDustRate(float rate)
    {
        if (rollingDust == null)
            return;

        ParticleSystem.EmissionModule emission = rollingDust.emission;
        emission.enabled = true;
        emission.rateOverTime = rate;
    }
}