using UnityEngine;

public class RaceCheckpoint : MonoBehaviour
{
    [SerializeField] private int checkpointIndex;
    [SerializeField] private Collider checkpointCollider;

    public int CheckpointIndex => checkpointIndex;

    private void Awake()
    {
        if (checkpointCollider == null)
        {
            Debug.LogError("RaceCheckpoint: Checkpoint Collider is not assigned.", this);
            return;
        }

        checkpointCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!RacePlayerController.TryResolve(other, out RacePlayerController player))
            return;

        player.ReachedCheckpoint(this);
    }
}
