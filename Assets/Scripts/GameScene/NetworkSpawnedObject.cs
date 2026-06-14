using Fusion;
using UnityEngine;

public class NetworkSpawnedObject : NetworkBehaviour
{
    [Header("Life Time")]
    [SerializeField] private float lifeTime = 10f;

    [Header("Visuals")]
    [SerializeField] private GameObject visualsRoot;
    [SerializeField] private Collider objectCollider;

    [Networked] public PlayerRef Owner { get; set; }
    [Networked] private TickTimer LifeTimer { get; set; }

    private bool isHidden;

    public override void Spawned()
    {
        isHidden = false;

        if (visualsRoot) visualsRoot.SetActive(true);

        if (objectCollider) objectCollider.enabled = true;

        if (!Object.HasStateAuthority) return;

        LifeTimer = TickTimer.CreateFromSeconds(Runner, lifeTime);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        if (LifeTimer.Expired(Runner))
        {
            Runner.Despawn(Object);
        }
    }

    public void RequestDespawn()
    {
        if (Runner.LocalPlayer != Owner) return;

        HideLocally();

        RPC_RequestDespawn();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDespawn(RpcInfo info = default)
    {
        if (info.Source != Owner) return;

        HideLocally();

        Runner.Despawn(Object);
    }

    private void HideLocally()
    {
        if (isHidden) return;

        isHidden = true;

        if (visualsRoot) visualsRoot.SetActive(false);

        if (objectCollider) objectCollider.enabled = false;
    }
}