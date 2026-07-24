using UnityEngine;

public class RaceTrackSurface : MonoBehaviour
{
    [SerializeField] private bool playerIsOnTrackInside = true;
    [SerializeField] private Collider surfaceCollider;

    private void Awake()
    {
        if (surfaceCollider == null)
        {
            Debug.LogError("RaceTrackSurface: Surface Collider is not assigned.", this);
            return;
        }

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
        if (RacePlayerController.TryResolve(other, out RacePlayerController player))
            player.SetOnTrack(isOnTrack);
    }
}
