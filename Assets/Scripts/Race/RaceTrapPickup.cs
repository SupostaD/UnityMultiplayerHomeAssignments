using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class RaceTrapPickup : NetworkBehaviour
{
    [Header("Rules")]
    [SerializeField, Min(0.1f)] private float respawnSeconds = 6f;
    [SerializeField, Min(0.1f)] private float pickupValidationDistance = 3f;

    [Header("References")]
    [SerializeField] private Collider triggerCollider;
    [SerializeField] private Renderer[] visuals;

    [Networked, OnChangedRender(nameof(OnAvailabilityChanged))]
    public NetworkBool IsAvailable { get; private set; }

    [Networked] private TickTimer RespawnTimer { get; set; }

    private void Reset()
    {
        ConfigureCollider();
    }

    private void Awake()
    {
        ConfigureCollider();
    }

    public override void Spawned()
    {
        if (visuals == null || visuals.Length == 0)
            visuals = GetComponentsInChildren<Renderer>();

        if (Object.HasStateAuthority) IsAvailable = true;

        RefreshVisuals();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        if (IsAvailable) return;

        if (!RespawnTimer.Expired(Runner)) return;

        IsAvailable = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsAvailable) return;

        RacePlayerController player = other.GetComponentInParent<RacePlayerController>();

        if (!player || !player.Object) return;

        if (!player.Object.HasStateAuthority) return;

        if (!player.TryGrantTrap()) return;

        RequestConsume(player.Object.InputAuthority, player.transform.position);
    }

    private void RequestConsume(PlayerRef player, Vector3 playerPosition)
    {
        if (Object.HasStateAuthority)
        {
            Consume(player, playerPosition); return;
        }

        RPC_RequestConsume(player, playerPosition);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestConsume(PlayerRef player, Vector3 playerPosition, RpcInfo info = default)
    {
        Consume(player, playerPosition);
    }

    private void Consume(PlayerRef player, Vector3 playerPosition)
    {
        if (!Object.HasStateAuthority) return;

        if (!CanConsume(player, playerPosition)) return;

        IsAvailable = false;
        RespawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, respawnSeconds));
    }

    private bool CanConsume(PlayerRef player, Vector3 playerPosition)
    {
        if (!IsAvailable) return false;

        float maxDistance = Mathf.Max(0.1f, pickupValidationDistance);
        float sqrDistance = (transform.position - playerPosition).sqrMagnitude;

        return sqrDistance <= maxDistance * maxDistance;
    }

    private void OnAvailabilityChanged()
    {
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        bool isAvailable = IsAvailable;

        if (triggerCollider) triggerCollider.enabled = isAvailable;

        if (visuals == null) return;

        foreach (Renderer currentRenderer in visuals)
        {
            if (!currentRenderer) continue;

            currentRenderer.enabled = isAvailable;
        }
    }

    private void ConfigureCollider()
    {
        triggerCollider = GetComponent<Collider>();

        if (triggerCollider) triggerCollider.isTrigger = true;
    }
}