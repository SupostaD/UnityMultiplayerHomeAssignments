using Fusion;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HexAbilityPickup : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private HexAbilitySettings settings;

    [Header("Ability")]
    [SerializeField] private HexPlayerAbilityType abilityType;

    [Header("References")]
    [SerializeField] private Collider triggerCollider;
    [SerializeField] private Renderer[] visuals;
    [SerializeField] private Image[] abilityImages;

    [Networked, OnChangedRender(nameof(OnAvailabilityChanged))]
    public NetworkBool IsAvailable { get; private set; }

    [Networked]
    private PlayerRef ReservedPlayer { get; set; }

    [Networked]
    private TickTimer ReservationTimer { get; set; }

    [Networked]
    private TickTimer RespawnTimer { get; set; }

    [Networked]
    private Vector3 SynchronizedSpawnPosition { get; set; }

    [Networked]
    private NetworkBool HasSynchronizedSpawnPosition { get; set; }

    public HexPlayerAbilityType AbilityType => abilityType;

    private void Awake()
    {
        ValidateReferences();
    }

    public override void Spawned()
    {
        ApplySynchronizedSpawnPosition();

        if (Object.HasStateAuthority)
        {
            IsAvailable = true;
            ReservedPlayer = PlayerRef.None;
            ReservationTimer = default;
            RespawnTimer = default;
        }

        RefreshVisuals();
    }

    public void InitializeSpawnPosition(Vector3 spawnPosition)
    {
        SynchronizedSpawnPosition = spawnPosition;
        HasSynchronizedSpawnPosition = true;
        transform.position = spawnPosition;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        if (ReservedPlayer != PlayerRef.None &&
            ReservationTimer.Expired(Runner))
        {
            ReservedPlayer = PlayerRef.None;
            ReservationTimer = default;
        }

        if (IsAvailable ||
            GetRespawnSeconds() <= 0f ||
            !RespawnTimer.Expired(Runner))
        {
            return;
        }

        IsAvailable = true;
        RespawnTimer = default;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsAvailable ||
            !HexBallPlayerController.TryResolvePlayer(
                other,
                out HexBallPlayerController player) ||
            player.Object == null ||
            !player.Object.HasStateAuthority ||
            player.IsEliminated)
        {
            return;
        }

        RequestCollection(
            player.Object.InputAuthority,
            player.transform.position
        );
    }

    private void RequestCollection(
        PlayerRef player,
        Vector3 reportedPosition)
    {
        if (Object.HasStateAuthority)
        {
            TryReserveAndOffer(
                player,
                reportedPosition
            );
            return;
        }

        RPC_RequestCollection(reportedPosition);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestCollection(
        Vector3 reportedPosition,
        RpcInfo info = default)
    {
        TryReserveAndOffer(
            info.Source,
            reportedPosition
        );
    }

    private void TryReserveAndOffer(
        PlayerRef player,
        Vector3 reportedPosition)
    {
        if (!Object.HasStateAuthority ||
            !IsAvailable ||
            ReservedPlayer != PlayerRef.None ||
            player == PlayerRef.None ||
            !ValidateDistance(reportedPosition) ||
            !Runner.TryGetPlayerObject(
                player,
                out NetworkObject playerObject) ||
            !NetworkObjectBehaviourReferences.TryGet(
                playerObject,
                out HexBallPlayerController playerController) ||
            playerController.IsEliminated)
        {
            return;
        }

        ReservedPlayer = player;
        ReservationTimer = TickTimer.CreateFromSeconds(
            Runner,
            settings != null
                ? settings.ReservationTimeoutSeconds
                : 1f
        );

        RPC_OfferAbility(player, abilityType);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OfferAbility(
        [RpcTarget] PlayerRef targetPlayer,
        HexPlayerAbilityType offeredAbility)
    {
        bool accepted = false;

        if (Runner != null &&
            Runner.TryGetPlayerObject(
                targetPlayer,
                out NetworkObject playerObject) &&
            NetworkObjectBehaviourReferences.TryGet(
                playerObject,
                out HexBallPlayerController playerController) &&
            playerController.Object.HasStateAuthority &&
            !playerController.IsEliminated)
        {
            accepted =
                playerController.TryGrantAbility(offeredAbility);
        }

        RPC_ReportOfferResult(accepted);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportOfferResult(
        NetworkBool accepted,
        RpcInfo info = default)
    {
        if (!Object.HasStateAuthority ||
            info.Source != ReservedPlayer)
        {
            return;
        }

        ReservedPlayer = PlayerRef.None;
        ReservationTimer = default;

        if (!accepted || !IsAvailable)
            return;

        IsAvailable = false;

        float respawnSeconds = GetRespawnSeconds();

        if (respawnSeconds > 0f)
        {
            RespawnTimer = TickTimer.CreateFromSeconds(
                Runner,
                respawnSeconds
            );
        }
        else
        {
            RespawnTimer = default;
        }
    }

    private bool ValidateDistance(Vector3 reportedPosition)
    {
        float maximumDistance =
            settings != null
                ? settings.PickupValidationDistance
                : 3f;

        return (transform.position - reportedPosition).sqrMagnitude <=
               maximumDistance * maximumDistance;
    }

    private void OnAvailabilityChanged()
    {
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        bool isAvailable = IsAvailable;

        if (triggerCollider != null)
            triggerCollider.enabled = isAvailable;

        if (visuals != null)
        {
            foreach (Renderer currentRenderer in visuals)
            {
                if (currentRenderer != null)
                    currentRenderer.enabled = isAvailable;
            }
        }

        if (abilityImages != null)
        {
            foreach (Image abilityImage in abilityImages)
            {
                if (abilityImage != null)
                    abilityImage.enabled = isAvailable;
            }
        }
    }

    private void ValidateReferences()
    {
        if (settings == null)
        {
            Debug.LogError(
                "HexAbilityPickup: Ability Settings is not assigned.",
                this
            );
        }

        if (triggerCollider == null)
        {
            Debug.LogError(
                "HexAbilityPickup: Trigger Collider is not assigned.",
                this
            );
        }
        else if (!triggerCollider.isTrigger)
        {
            Debug.LogError(
                "HexAbilityPickup: Trigger Collider must have Is Trigger enabled.",
                this
            );
        }

        bool hasRendererVisuals =
            visuals != null &&
            visuals.Length > 0;
        bool hasImageVisuals =
            abilityImages != null &&
            abilityImages.Length > 0;

        if (!hasRendererVisuals && !hasImageVisuals)
        {
            Debug.LogError(
                "HexAbilityPickup: Neither Visual Renderers nor Ability Images are assigned.",
                this
            );
        }
    }

    private float GetRespawnSeconds()
    {
        return settings != null
            ? settings.PickupRespawnSeconds
            : 10f;
    }

    private void ApplySynchronizedSpawnPosition()
    {
        if (HasSynchronizedSpawnPosition)
            transform.position = SynchronizedSpawnPosition;
    }
}
