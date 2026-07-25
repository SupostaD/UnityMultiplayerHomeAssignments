using UnityEngine;

public class BallJumpVFX : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HexBallPlayerController controller;

    [Header("Particles")]
    [SerializeField] private ParticleSystem takeoffDust;
    [SerializeField] private ParticleSystem landingDust;
    [SerializeField] private ParticleSystem doubleJumpBurst;

    private bool initialized;
    private bool wasGrounded;

    private void LateUpdate()
    {
        if (!controller || controller.IsEliminated) 
            return;

        bool grounded = controller.IsGroundedForVFX();

        if (!initialized)
        {
            initialized = true;
            wasGrounded = grounded;
            return;
        }

        if (wasGrounded && !grounded)
        {
            takeoffDust?.Play();
        }
        else if (!wasGrounded && grounded)
        {
            landingDust?.Play();
        }

        wasGrounded = grounded;
    }

    public void PlayDoubleJump()
    {
        doubleJumpBurst?.Play();
    }
}