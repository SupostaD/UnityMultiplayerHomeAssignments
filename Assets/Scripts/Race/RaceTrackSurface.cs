using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RaceTrackSurface : MonoBehaviour
{
    [SerializeField] private bool playerIsOnTrackInside = true;

    private void Reset()
    {
        Collider surfaceCollider = GetComponent<Collider>();

        if (surfaceCollider != null)
            surfaceCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        SetPlayerTrackState(other, playerIsOnTrackInside);
    }

    private void OnTriggerExit(Collider other)
    {
        SetPlayerTrackState(other, !playerIsOnTrackInside);
    }

    private static void SetPlayerTrackState(Collider other, bool isOnTrack)
    {
        RacePlayerController player = other.GetComponentInParent<RacePlayerController>();

        if (player != null)
            player.SetOnTrack(isOnTrack);
    }
}
