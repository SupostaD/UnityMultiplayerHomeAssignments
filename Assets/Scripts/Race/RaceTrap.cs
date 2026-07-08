using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class RaceTrap : NetworkBehaviour
{
    [Header("Rules")]
    [SerializeField, Min(0.1f)] private float lifeSeconds = 8f;
    [SerializeField, Min(0.1f)] private float hitDespawnValidationDistance = 4f;

    [Header("References")]
    [SerializeField] private Collider triggerCollider;
    [SerializeField] private Renderer[] visuals;

    [Networked] public PlayerRef Owner { get; private set; }

    [Networked, OnChangedRender(nameof(OnOwnerCharacterIndexChanged))]
    public int OwnerCharacterIndex { get; private set; }

    [Networked] private TickTimer LifeTimer { get; set; }

    private Rigidbody cachedRigidbody;
    private bool visualsHiddenLocally;

    private void Awake()
    {
        ConfigurePhysics();
    }

    private void Reset()
    {
        ConfigurePhysics();
    }

    public override void Spawned()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (visuals == null || visuals.Length == 0)
            visuals = GetComponentsInChildren<Renderer>();

        ShowVisualsLocally();
        ApplyTrapColor();

        if (Object.HasStateAuthority)
            LifeTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, lifeSeconds));

        Debug.Log(
            "RaceTrap Spawned on client." +
            " Position: " + transform.position +
            " | Owner: " + Owner +
            " | Owner Character Index: " + OwnerCharacterIndex +
            " | HasStateAuthority: " + Object.HasStateAuthority +
            " | HasInputAuthority: " + Object.HasInputAuthority
        );
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        if (LifeTimer.Expired(Runner))
            Runner.Despawn(Object);
    }

    public void Initialize(PlayerRef owner, int ownerCharacterIndex)
    {
        Owner = owner;
        OwnerCharacterIndex = ownerCharacterIndex;
        ApplyTrapColor();
    }

    private void OnOwnerCharacterIndexChanged()
    {
        ApplyTrapColor();
    }

    private void ApplyTrapColor()
    {
        if (visuals == null || visuals.Length == 0)
            visuals = GetComponentsInChildren<Renderer>();

        Color trapColor = Color.white;

        if (GameSceneManager.Instance != null)
            trapColor = GameSceneManager.Instance.GetCharacterColor(OwnerCharacterIndex);

        foreach (Renderer currentRenderer in visuals)
        {
            if (currentRenderer == null)
                continue;

            currentRenderer.material.color = trapColor;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Trap trigger entered by: " + other.name);

        RacePlayerController hitPlayer = other.GetComponentInParent<RacePlayerController>();

        if (hitPlayer == null || hitPlayer.Object == null)
        {
            Debug.Log("Trap trigger ignored: no RacePlayerController.");
            return;
        }

        Debug.Log(
            "Trap touched player." +
            " Hit player: " + hitPlayer.Object.InputAuthority +
            " | Trap owner: " + Owner +
            " | Hit player has StateAuthority here: " + hitPlayer.Object.HasStateAuthority
        );

        if (hitPlayer.Object.InputAuthority == Owner)
        {
            Debug.Log("Trap ignored owner.");
            return;
        }

        if (!hitPlayer.Object.HasStateAuthority)
        {
            Debug.Log("Trap ignored because this client is not the hit player's StateAuthority.");
            return;
        }

        if (!hitPlayer.TryReceiveTrapHit(Owner, transform.position))
        {
            Debug.Log("Trap hit rejected by player validation.");
            return;
        }

        Debug.Log("Trap hit accepted.");
        
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.AddHitPoint(Owner);

        HideVisualsLocally();
        RequestDespawnAfterHit(hitPlayer.Object.InputAuthority, hitPlayer.transform.position);
    }

    private void RequestDespawnAfterHit(PlayerRef hitPlayer, Vector3 hitPosition)
    {
        if (Object.HasStateAuthority)
        {
            DespawnAfterHit(hitPlayer, hitPosition);
            return;
        }

        RPC_RequestDespawnAfterHit(hitPlayer, hitPosition);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDespawnAfterHit(PlayerRef hitPlayer, Vector3 hitPosition, RpcInfo info = default)
    {
        DespawnAfterHit(hitPlayer, hitPosition);
    }

    private void DespawnAfterHit(PlayerRef hitPlayer, Vector3 hitPosition)
    {
        if (!Object.HasStateAuthority)
            return;

        if (!CanDespawnAfterHit(hitPlayer, hitPosition))
            return;

        Runner.Despawn(Object);
    }

    private bool CanDespawnAfterHit(PlayerRef hitPlayer, Vector3 hitPosition)
    {
        if (hitPlayer == Owner)
            return false;

        float maxDistance = Mathf.Max(0.1f, hitDespawnValidationDistance);
        float sqrDistance = (transform.position - hitPosition).sqrMagnitude;

        return sqrDistance <= maxDistance * maxDistance;
    }

    private void ConfigurePhysics()
    {
        triggerCollider = GetComponent<Collider>();

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        cachedRigidbody = GetComponent<Rigidbody>();

        if (cachedRigidbody == null)
            return;

        cachedRigidbody.isKinematic = true;
        cachedRigidbody.useGravity = false;
        cachedRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void HideVisualsLocally()
    {
        if (visualsHiddenLocally)
            return;

        SetVisualsLocally(false);
        visualsHiddenLocally = true;
    }

    private void ShowVisualsLocally()
    {
        SetVisualsLocally(true);
        visualsHiddenLocally = false;
    }

    private void SetVisualsLocally(bool visible)
    {
        if (visuals == null)
            return;

        foreach (Renderer currentRenderer in visuals)
        {
            if (currentRenderer == null)
                continue;

            currentRenderer.enabled = visible;
        }
    }
}