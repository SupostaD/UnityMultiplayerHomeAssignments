using System.Collections;
using UnityEngine;

public class BallDashVFX : MonoBehaviour
{
    [Header("Particles")]
    [SerializeField] private ParticleSystem dashBurst;
    [SerializeField] private Transform dashBurstRoot;
    
    [Header("Trails")]
    [SerializeField] private TrailRenderer[] trails;
    [SerializeField, Min(0.01f)] private float trailSeconds = 0.25f;
    
    private Coroutine trailRoutine;

    public void PlayDash(Vector3 dashDirection)
    {
        if (dashDirection.sqrMagnitude > 0.001f && dashBurst != null)
        {
            Vector3 backwardDirection = -dashDirection.normalized;
            dashBurstRoot.rotation = Quaternion.LookRotation(backwardDirection, Vector3.up);
        }
        
        dashBurst?.Play();
        
        if (trailRoutine != null)
            StopCoroutine(trailRoutine);

        trailRoutine = StartCoroutine(TrailRoutine());
    }

    private IEnumerator TrailRoutine()
    {
        SetTrails(true);
        
        yield return new WaitForSeconds(trailSeconds);

        SetTrails(false);
        trailRoutine = null;
    }

    private void SetTrails(bool isEnabled)
    {
        if (trails == null)
            return;

        foreach (TrailRenderer trail in trails)
        {
            if (trail ==  null)
                continue;
            
            if (isEnabled)
                trail.Clear();

            trail.emitting = isEnabled;
        }
    }
}
