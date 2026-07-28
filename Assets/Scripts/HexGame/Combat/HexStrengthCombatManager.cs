using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class HexStrengthCombatManager : NetworkBehaviour
{
    [Header("Required References")]
    [SerializeField] private HexTerritoryManager territoryManager;
    [SerializeField] private HexStrengthCombatSettings settings;

    private sealed class ActiveContact
    {
        public PlayerRef FirstPlayer;
        public PlayerRef SecondPlayer;
        public TickTimer ContactExpiry;
        public TickTimer NextTransfer;
        public float DrainProgress;
    }

    private sealed class PendingImpact
    {
        public PlayerRef FirstPlayer;
        public PlayerRef SecondPlayer;
        public bool HasFirstReport;
        public bool HasSecondReport;
        public Vector3 FirstReportedVelocity;
        public Vector3 SecondReportedVelocity;
        public bool FirstReportedDash;
        public bool SecondReportedDash;
        public int FirstReportSequence;
        public int SecondReportSequence;
        public Vector3 NormalAccumulator;
        public int NormalSampleCount;
        public TickTimer CollectionTimer;
        public TickTimer ExpiryTimer;
    }

    private readonly Dictionary<long, ActiveContact> activeContacts =
        new Dictionary<long, ActiveContact>();
    private readonly Dictionary<long, TickTimer> impactCooldowns =
        new Dictionary<long, TickTimer>();
    private readonly Dictionary<long, PendingImpact> pendingImpacts =
        new Dictionary<long, PendingImpact>();
    private readonly List<long> contactsToRemove = new List<long>();
    private readonly List<long> impactsToRemove = new List<long>();

    public float ContactReportIntervalSeconds =>
        settings != null
            ? settings.ContactReportIntervalSeconds
            : 0.05f;

    public override void Spawned()
    {
        if (territoryManager == null)
        {
            Debug.LogError(
                "HexStrengthCombatManager: Territory Manager is not assigned.",
                this
            );
        }

        if (settings == null)
        {
            Debug.LogError(
                "HexStrengthCombatManager: Strength Combat Settings is not assigned.",
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

            float interval = settings != null
                ? settings.TransferIntervalSeconds
                : 0.05f;
            int drainPerSecond = settings != null
                ? settings.StrengthDrainPerSecond
                : 6;
            contact.DrainProgress +=
                drainPerSecond * interval;
            int transferAmount =
                Mathf.FloorToInt(contact.DrainProgress);

            if (transferAmount > 0)
            {
                contact.DrainProgress -= transferAmount;
                territoryManager.TryTransferStrength(
                    contact.FirstPlayer,
                    contact.SecondPlayer,
                    transferAmount
                );
            }

            contact.NextTransfer = TickTimer.CreateFromSeconds(
                Runner,
                interval
            );
        }

        foreach (long contactKey in contactsToRemove)
            activeContacts.Remove(contactKey);

        ProcessPendingImpacts();
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

    public void RequestImpact(
        PlayerRef otherPlayer,
        Vector3 reporterPosition,
        Vector3 reporterVelocity,
        Vector3 directionToOther,
        bool reporterIsDashing,
        int reportSequence)
    {
        if (Runner == null ||
            Object == null ||
            otherPlayer == PlayerRef.None)
        {
            return;
        }

        RPC_ReportImpact(
            otherPlayer,
            reporterPosition,
            reporterVelocity,
            directionToOther,
            reporterIsDashing,
            reportSequence
        );
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportImpact(
        PlayerRef otherPlayer,
        Vector3 reporterPosition,
        Vector3 reporterVelocity,
        Vector3 directionToOther,
        bool reporterIsDashing,
        int reportSequence,
        RpcInfo info = default)
    {
        PlayerRef reportingPlayer = info.Source;

        if (reportingPlayer == PlayerRef.None ||
            otherPlayer == PlayerRef.None ||
            reportingPlayer == otherPlayer)
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

        if (impactCooldowns.TryGetValue(
                contactKey,
                out TickTimer cooldown) &&
            !cooldown.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        if (!ValidateReportedContact(
                reportingPlayer,
                otherPlayer,
                reporterPosition))
        {
            RejectReportedPrediction(
                reportingPlayer,
                otherPlayer,
                reportSequence,
                reporterVelocity
            );
            return;
        }

        HexPlayerCollisionSettings settings = GetCollisionSettings();

        if (settings == null)
            return;

        if (!pendingImpacts.TryGetValue(
                contactKey,
                out PendingImpact pendingImpact))
        {
            pendingImpact = new PendingImpact
            {
                FirstPlayer = firstPlayer,
                SecondPlayer = secondPlayer,
                CollectionTimer = TickTimer.CreateFromSeconds(
                    Runner,
                    settings.ReportCollectionSeconds
                ),
                ExpiryTimer = TickTimer.CreateFromSeconds(
                    Runner,
                    settings.PendingImpactLifetimeSeconds
                )
            };
            pendingImpacts.Add(contactKey, pendingImpact);
        }

        reporterVelocity = ClampReportedVelocity(
            reporterVelocity,
            settings.MaximumReportedSpeed
        );
        directionToOther.y = 0f;

        if (directionToOther.sqrMagnitude > 0.0001f)
        {
            Vector3 canonicalNormal =
                reportingPlayer == firstPlayer
                    ? directionToOther.normalized
                    : -directionToOther.normalized;
            pendingImpact.NormalAccumulator += canonicalNormal;
            pendingImpact.NormalSampleCount++;
        }

        if (reportingPlayer == firstPlayer)
        {
            pendingImpact.HasFirstReport = true;
            pendingImpact.FirstReportedVelocity = reporterVelocity;
            pendingImpact.FirstReportedDash = reporterIsDashing;
            pendingImpact.FirstReportSequence =
                Mathf.Max(0, reportSequence);
        }
        else
        {
            pendingImpact.HasSecondReport = true;
            pendingImpact.SecondReportedVelocity = reporterVelocity;
            pendingImpact.SecondReportedDash = reporterIsDashing;
            pendingImpact.SecondReportSequence =
                Mathf.Max(0, reportSequence);
        }
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
        float interval = settings != null
            ? settings.TransferIntervalSeconds
            : 0.05f;
        float memorySeconds = settings != null
            ? settings.ContactMemorySeconds
            : 0.2f;

        if (!activeContacts.TryGetValue(contactKey, out ActiveContact contact))
        {
            contact = new ActiveContact
            {
                FirstPlayer = firstPlayer,
                SecondPlayer = secondPlayer,
                NextTransfer = TickTimer.CreateFromSeconds(
                    Runner,
                    interval
                )
            };
            activeContacts.Add(contactKey, contact);

            int immediateAmount = settings != null
                ? settings.InitialContactDrain
                : 1;

            if (immediateAmount > 0)
            {
                territoryManager.TryTransferStrength(
                    firstPlayer,
                    secondPlayer,
                    immediateAmount
                );
            }
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
            settings != null
                ? settings.MaximumContactDistance
                : 2.25f,
            combinedCollisionRadii + 0.25f
        );

        return (firstObject.transform.position - secondObject.transform.position)
               .sqrMagnitude <= maximumDistance * maximumDistance;
    }

    private bool ValidateReportedContact(
        PlayerRef reportingPlayer,
        PlayerRef otherPlayer,
        Vector3 reportedPosition)
    {
        if (Runner == null ||
            territoryManager == null ||
            territoryManager.IsPlayerEliminated(reportingPlayer) ||
            territoryManager.IsPlayerEliminated(otherPlayer))
        {
            return false;
        }

        NetworkObject reportingObject =
            Runner.GetPlayerObject(reportingPlayer);
        NetworkObject otherObject =
            Runner.GetPlayerObject(otherPlayer);

        if (!NetworkObjectBehaviourReferences.TryGet(
                reportingObject,
                out HexBallPlayerController reportingController) ||
            !NetworkObjectBehaviourReferences.TryGet(
                otherObject,
                out HexBallPlayerController otherController))
        {
            return false;
        }

        float combinedCollisionRadii =
            reportingController.CollisionRadius +
            otherController.CollisionRadius;
        HexPlayerCollisionSettings collisionSettings =
            GetCollisionSettings();
        float reportDrift =
            collisionSettings != null
                ? collisionSettings.MaximumReportPositionDrift
                : 0f;
        float allowedDistance = Mathf.Max(
            settings != null
                ? settings.MaximumContactDistance
                : 2.25f,
            combinedCollisionRadii + 0.25f
        ) + reportDrift;

        return (reportedPosition - otherObject.transform.position)
               .sqrMagnitude <= allowedDistance * allowedDistance;
    }

    private void ProcessPendingImpacts()
    {
        impactsToRemove.Clear();

        foreach (KeyValuePair<long, PendingImpact> pair in pendingImpacts)
        {
            PendingImpact pendingImpact = pair.Value;
            bool hasFreshFirstVelocity =
                pendingImpact.HasFirstReport ||
                pendingImpact.FirstPlayer == Runner.LocalPlayer;
            bool hasFreshSecondVelocity =
                pendingImpact.HasSecondReport ||
                pendingImpact.SecondPlayer == Runner.LocalPlayer;
            bool hasCompleteVelocitySample =
                hasFreshFirstVelocity &&
                hasFreshSecondVelocity;
            bool collectionFinished =
                pendingImpact.CollectionTimer.ExpiredOrNotRunning(Runner);
            bool impactExpired =
                pendingImpact.ExpiryTimer.ExpiredOrNotRunning(Runner);

            if ((!hasCompleteVelocitySample ||
                 !collectionFinished) &&
                !impactExpired)
            {
                continue;
            }

            if (TryResolveImpact(
                    pendingImpact,
                    out HexBallPlayerController firstController,
                    out HexBallPlayerController secondController,
                    out Vector3 firstResolvedVelocity,
                    out Vector3 secondResolvedVelocity,
                    out float verticalLift))
            {
                HexPlayerCollisionSettings settings =
                    GetCollisionSettings();

                if (settings != null)
                {
                    impactCooldowns[pair.Key] =
                        TickTimer.CreateFromSeconds(
                            Runner,
                            settings.ImpactCooldownSeconds
                        );
                    firstController.RequestResolvedImpactVelocity(
                        pendingImpact.SecondPlayer,
                        pendingImpact.FirstReportSequence,
                        firstResolvedVelocity,
                        verticalLift,
                        false
                    );
                    secondController.RequestResolvedImpactVelocity(
                        pendingImpact.FirstPlayer,
                        pendingImpact.SecondReportSequence,
                        secondResolvedVelocity,
                        verticalLift,
                        false
                    );
                }

                impactsToRemove.Add(pair.Key);
                continue;
            }

            if (hasCompleteVelocitySample || impactExpired)
            {
                RejectPendingPredictions(pendingImpact);
                impactsToRemove.Add(pair.Key);
            }
        }

        foreach (long impactKey in impactsToRemove)
            pendingImpacts.Remove(impactKey);
    }

    private bool TryResolveImpact(
        PendingImpact pendingImpact,
        out HexBallPlayerController firstController,
        out HexBallPlayerController secondController,
        out Vector3 firstResolvedVelocity,
        out Vector3 secondResolvedVelocity,
        out float verticalLift)
    {
        firstController = null;
        secondController = null;
        firstResolvedVelocity = Vector3.zero;
        secondResolvedVelocity = Vector3.zero;
        verticalLift = 0f;

        HexPlayerCollisionSettings settings = GetCollisionSettings();

        if (settings == null ||
            pendingImpact == null ||
            territoryManager == null ||
            territoryManager.IsPlayerEliminated(
                pendingImpact.FirstPlayer) ||
            territoryManager.IsPlayerEliminated(
                pendingImpact.SecondPlayer))
        {
            return false;
        }

        NetworkObject firstObject =
            Runner.GetPlayerObject(pendingImpact.FirstPlayer);
        NetworkObject secondObject =
            Runner.GetPlayerObject(pendingImpact.SecondPlayer);

        if (!NetworkObjectBehaviourReferences.TryGet(
                firstObject,
                out firstController) ||
            !NetworkObjectBehaviourReferences.TryGet(
                secondObject,
                out secondController))
        {
            return false;
        }

        Vector3 collisionNormal =
            pendingImpact.NormalSampleCount > 0
                ? pendingImpact.NormalAccumulator
                : secondController.transform.position -
                  firstController.transform.position;
        collisionNormal.y = 0f;

        if (collisionNormal.sqrMagnitude <= 0.0001f)
        {
            collisionNormal =
                secondController.NetworkVelocity -
                firstController.NetworkVelocity;
            collisionNormal.y = 0f;
        }

        if (collisionNormal.sqrMagnitude <= 0.0001f)
            collisionNormal = Vector3.right;

        collisionNormal.Normalize();

        Vector3 firstVelocity =
            pendingImpact.HasFirstReport
                ? pendingImpact.FirstReportedVelocity
                : firstController.CollisionVelocity;
        Vector3 secondVelocity =
            pendingImpact.HasSecondReport
                ? pendingImpact.SecondReportedVelocity
                : secondController.CollisionVelocity;
        bool firstIsDashing =
            (pendingImpact.HasFirstReport
                ? pendingImpact.FirstReportedDash
                : firstController.IsDashImpactActive);
        bool secondIsDashing =
            (pendingImpact.HasSecondReport
                ? pendingImpact.SecondReportedDash
                : secondController.IsDashImpactActive);

        return HexPlayerImpactResolver.TryResolve(
            settings,
            firstVelocity,
            secondVelocity,
            collisionNormal,
            firstController.CurrentMass,
            secondController.CurrentMass,
            firstIsDashing,
            secondIsDashing,
            out firstResolvedVelocity,
            out secondResolvedVelocity,
            out verticalLift
        );
    }

    private void RejectPendingPredictions(
        PendingImpact pendingImpact)
    {
        if (pendingImpact == null)
            return;

        if (pendingImpact.HasFirstReport)
        {
            RejectReportedPrediction(
                pendingImpact.FirstPlayer,
                pendingImpact.SecondPlayer,
                pendingImpact.FirstReportSequence,
                pendingImpact.FirstReportedVelocity
            );
        }

        if (pendingImpact.HasSecondReport)
        {
            RejectReportedPrediction(
                pendingImpact.SecondPlayer,
                pendingImpact.FirstPlayer,
                pendingImpact.SecondReportSequence,
                pendingImpact.SecondReportedVelocity
            );
        }
    }

    private void RejectReportedPrediction(
        PlayerRef reportingPlayer,
        PlayerRef otherPlayer,
        int reportSequence,
        Vector3 reportedVelocity)
    {
        if (Runner == null ||
            reportSequence <= 0)
        {
            return;
        }

        NetworkObject reportingObject =
            Runner.GetPlayerObject(reportingPlayer);

        if (!NetworkObjectBehaviourReferences.TryGet(
                reportingObject,
                out HexBallPlayerController reportingController))
        {
            return;
        }

        reportingController.RequestResolvedImpactVelocity(
            otherPlayer,
            reportSequence,
            reportedVelocity,
            0f,
            true
        );
    }

    private static Vector3 ClampReportedVelocity(
        Vector3 reportedVelocity,
        float maximumSpeed)
    {
        Vector3 planarVelocity = new Vector3(
            reportedVelocity.x,
            0f,
            reportedVelocity.z
        );
        planarVelocity = Vector3.ClampMagnitude(
            planarVelocity,
            Mathf.Max(0.1f, maximumSpeed)
        );
        return planarVelocity;
    }

    private static HexPlayerCollisionSettings GetCollisionSettings()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.PlayerCollisionSettings
            : null;
    }

    private static long BuildContactKey(
        PlayerRef firstPlayer,
        PlayerRef secondPlayer)
    {
        return ((long)firstPlayer.PlayerId << 32) |
               (uint)secondPlayer.PlayerId;
    }
}
