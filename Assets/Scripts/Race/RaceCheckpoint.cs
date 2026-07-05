using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RaceCheckpoint : MonoBehaviour
{
    [SerializeField] private int checkpointIndex;

    public int CheckpointIndex => checkpointIndex;

    private void Reset()
    {
        Collider checkpointCollider = GetComponent<Collider>();

        if (checkpointCollider != null)
            checkpointCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        RacePlayerController player = other.GetComponentInParent<RacePlayerController>();

        if (player == null)
            return;

        player.ReachedCheckpoint(this);
    }
}
