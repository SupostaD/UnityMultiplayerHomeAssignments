using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class HexStrengthCombatManager : NetworkBehaviour
{
    [Header("Required References")]
    [SerializeField] private HexTerritoryManager territoryManager;

    [Header("Strength Drain")]
    [SerializeField, Min(0.01f)] private float strengthDrainPerSecond = 1f;
    [SerializeField, Min(0.02f)] private float transferIntervalSeconds = 0.1f;
    [SerializeField, Min(0.05f)] private float contactMemorySeconds = 0.3f;

    [Header("Master Validation")]
    [SerializeField, Min(0.1f)] private float maximumContactDistance = 2.25f;

    private sealed class ActiveContact
    {
        public PlayerRef FirstPlayer;
        public PlayerRef SecondPlayer;
        public TickTimer ContactExpiry;
        public TickTimer NextTransfer;
    }

    private readonly Dictionary<long, ActiveContact> activeContacts =
        new Dictionary<long, ActiveContact>();
    private readonly List<long> contactsToRemove = new List<long>();

    public override void Spawned()
    {
        if (territoryManager == null)
        {
            Debug.LogError(
                "HexStrengthCombatManager: Territory Manager is not assigned.",
                this
            );
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null ||
            !Object.HasStateAuthority ||
            territoryManager == null)
        {
            return;
        }

        contactsToRemove.Clear();

        foreach (KeyValuePair<long, ActiveContact> pair in activeContacts)
        {
            ActiveContact contact = pair.Value;

            if (contact.ContactExpiry.Expired(Runner) ||
                !ValidateContact(contact.FirstPlayer, contact.SecondPlayer))
            {
                contactsToRemove.Add(pair.Key);
                continue;
            }

            if (!contact.NextTransfer.ExpiredOrNotRunning(Runner))
                continue;

            float interval = Mathf.Max(0.02f, transferIntervalSeconds);
            float transferAmount =
                Mathf.Max(0.01f, strengthDrainPerSecond) * interval;

            ApplyMassPush(
                contact.FirstPlayer,
                contact.SecondPlayer,
                interval
            );
            territoryManager.TryTransferStrength(
                contact.FirstPlayer,
                contact.SecondPlayer,
                transferAmount
            );
            contact.NextTransfer = TickTimer.CreateFromSeconds(
                Runner,
                interval
            );
        }

        foreach (long contactKey in contactsToRemove)
            activeContacts.Remove(contactKey);
    }

    public void RequestContact(PlayerRef otherPlayer)
    {
        if (Runner == null ||
            Object == null ||
            otherPlayer == PlayerRef.None)
        {
            return;
        }

        RPC_ReportContact(otherPlayer);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportContact(
        PlayerRef otherPlayer,
        RpcInfo info = default)
    {
        PlayerRef reportingPlayer = info.Source;

        if (reportingPlayer == PlayerRef.None ||
            otherPlayer == PlayerRef.None ||
            reportingPlayer == otherPlayer ||
            !ValidateContact(reportingPlayer, otherPlayer))
        {
            return;
        }

        PlayerRef firstPlayer = reportingPlayer.PlayerId < otherPlayer.PlayerId
            ? reportingPlayer
            : otherPlayer;
        PlayerRef secondPlayer = reportingPlayer.PlayerId < otherPlayer.PlayerId
            ? otherPlayer
            : reportingPlayer;
        long contactKey = BuildContactKey(firstPlayer, secondPlayer);
        float memorySeconds = Mathf.Max(
            Mathf.Max(0.05f, contactMemorySeconds),
            Mathf.Max(0.02f, transferIntervalSeconds) * 2f
        );

        if (!activeContacts.TryGetValue(contactKey, out ActiveContact contact))
        {
            contact = new ActiveContact
            {
                FirstPlayer = firstPlayer,
                SecondPlayer = secondPlayer,
                NextTransfer = TickTimer.CreateFromSeconds(
                    Runner,
                    Mathf.Max(0.02f, transferIntervalSeconds)
                )
            };
            activeContacts.Add(contactKey, contact);
        }

        contact.ContactExpiry = TickTimer.CreateFromSeconds(
            Runner,
            memorySeconds
        );
    }

    private bool ValidateContact(
        PlayerRef firstPlayer,
        PlayerRef secondPlayer)
    {
        if (Runner == null ||
            territoryManager == null ||
            territoryManager.IsPlayerEliminated(firstPlayer) ||
            territoryManager.IsPlayerEliminated(secondPlayer))
        {
            return false;
        }

        NetworkObject firstObject = Runner.GetPlayerObject(firstPlayer);
        NetworkObject secondObject = Runner.GetPlayerObject(secondPlayer);

        if (!NetworkObjectBehaviourReferences.TryGet(
                firstObject,
                out HexBallPlayerController firstController) ||
            !NetworkObjectBehaviourReferences.TryGet(
                secondObject,
                out HexBallPlayerController secondController))
        {
            return false;
        }

        float combinedCollisionRadii =
            firstController.CollisionRadius +
            secondController.CollisionRadius;
        float maximumDistance = Mathf.Max(
            Mathf.Max(0.1f, maximumContactDistance),
            combinedCollisionRadii + 0.25f
        );

        return (firstObject.transform.position - secondObject.transform.position)
               .sqrMagnitude <= maximumDistance * maximumDistance;
    }

    private void ApplyMassPush(
        PlayerRef firstPlayer,
        PlayerRef secondPlayer,
        float interval)
    {
        NetworkObject firstObject = Runner.GetPlayerObject(firstPlayer);
        NetworkObject secondObject = Runner.GetPlayerObject(secondPlayer);

        if (!NetworkObjectBehaviourReferences.TryGet(
                firstObject,
                out HexBallPlayerController firstController) ||
            !NetworkObjectBehaviourReferences.TryGet(
                secondObject,
                out HexBallPlayerController secondController))
        {
            return;
        }

        float firstMass = firstController.CurrentMass;
        float secondMass = secondController.CurrentMass;

        if (Mathf.Approximately(firstMass, secondMass))
            return;

        HexBallPlayerController heavierController =
            firstMass > secondMass
                ? firstController
                : secondController;
        HexBallPlayerController lighterController =
            firstMass > secondMass
                ? secondController
                : firstController;
        float heavierMass = Mathf.Max(firstMass, secondMass);
        Vector3 pushDirection =
            lighterController.transform.position -
            heavierController.transform.position;

        lighterController.RequestMassPush(
            pushDirection,
            heavierMass,
            interval
        );
    }

    private static long BuildContactKey(
        PlayerRef firstPlayer,
        PlayerRef secondPlayer)
    {
        return ((long)firstPlayer.PlayerId << 32) |
               (uint)secondPlayer.PlayerId;
    }
}
