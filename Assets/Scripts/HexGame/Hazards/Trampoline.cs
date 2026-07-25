using UnityEngine;

public class Trampoline : MonoBehaviour
{
    [Header("Launch")]
    [SerializeField, Min(0.1f)] private float verticalSpeed = 11f;
    [SerializeField, Min(0f)] private float forwardVelocityChange = 5f;

    [Header("Temporary Speed Boost")]
    [SerializeField, Min(1f)] private float speedMultiplier = 1.6f;
    [SerializeField, Min(0.05f)] private float boostDuration = 1.25f;

    [Header("Optional Direction")]
    [SerializeField] private Transform launchDirection;

    [Header("Juice")]
    [SerializeField] private ParticleSystem bounceParticles;
    private void OnTriggerEnter(Collider other)
    {
        if (!HexBallPlayerController.TryResolvePlayer(
                other, out HexBallPlayerController player))
            return;

        PlayBounceFeedback();

        if (player.Object == null || !player.Object.HasStateAuthority) 
            return;

        Vector3 direction = launchDirection != null
                ? launchDirection.forward
                : transform.forward;

        player.ApplyTrampolineLaunch(direction, verticalSpeed, 
            forwardVelocityChange, speedMultiplier, boostDuration);
    }

    private void PlayBounceFeedback()
    {
        if (bounceParticles) bounceParticles?.Play();
    }
}
