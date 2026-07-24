using UnityEngine;

[DisallowMultipleComponent]
public sealed class HexStrengthDeathZone : MonoBehaviour
{
    [Header("Required Reference")]
    [SerializeField] private HexTerritoryManager territoryManager;

    private void Awake()
    {
        if (territoryManager == null)
        {
            Debug.LogError(
                "HexStrengthDeathZone: Territory Manager is not assigned.",
                this
            );
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (territoryManager == null ||
            !HexBallPlayerController.TryResolvePlayer(
                other,
                out HexBallPlayerController player) ||
            player.Object == null ||
            !player.Object.HasStateAuthority ||
            player.IsEliminated)
        {
            return;
        }

        territoryManager.RequestDeathZonePenalty();
    }
}
