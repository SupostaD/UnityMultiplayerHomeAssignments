using Fusion;
using UnityEngine;

public class NetworkPlayerCharacter : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 720f;

    [Header("Visuals")]
    [SerializeField] private Renderer[] renderers;

    [Header("Spawned Objects")]
    [SerializeField] private NetworkObject spawnedObjectPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float destroyRange = 2.5f;

    [Networked, OnChangedRender(nameof(OnCharacterIndexChanged))]
    public int CharacterIndex { get; set; }

    private bool usesRaceController;

    private void Awake()
    {
        usesRaceController = GetComponent("RacePlayerController") != null;
    }

    public override void Spawned()
    {
        usesRaceController = GetComponent("RacePlayerController") != null;
        ApplyCharacterColor();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        if (usesRaceController)
            return;

        if (GameInputBlocker.IsGameplayInputBlocked)
            return;

        Move();

        if (Input.GetKeyDown(KeyCode.F))
            SpawnObject();

        if (Input.GetKeyDown(KeyCode.G))
            TryDestroyNearbyObject();
    }

    private void Move()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 input = new Vector3(horizontal, vertical, 0f);

        if (input.sqrMagnitude > 1f) input.Normalize();

        transform.position += input * moveSpeed * Runner.DeltaTime;

        if (input.sqrMagnitude <= 0.001f) return;

        float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.Euler(0f, 0f, angle),
            rotationSpeed * Runner.DeltaTime
        );
    }

    private void SpawnObject()
    {
        if (!spawnedObjectPrefab) return;

        if (!spawnPoint)
        {
            Debug.LogError("Spawn Point is not assigned.");
            return;
        }

        NetworkObject spawnedObject = Runner.Spawn(
            spawnedObjectPrefab,
            spawnPoint.position,
            spawnPoint.rotation,
            Object.InputAuthority
        );

        NetworkSpawnedObject networkSpawnedObject =
            spawnedObject.GetComponent<NetworkSpawnedObject>();

        if (networkSpawnedObject) networkSpawnedObject.Owner = Object.InputAuthority;
    }

    private void TryDestroyNearbyObject()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, destroyRange);

        foreach (Collider hit in hits)
        {
            NetworkSpawnedObject spawnedObject =
                hit.GetComponentInParent<NetworkSpawnedObject>();

            if (!spawnedObject) continue;

            spawnedObject.RequestDespawn();
            return;
        }
    }

    private void OnCharacterIndexChanged()
    {
        ApplyCharacterColor();
    }

    private void ApplyCharacterColor()
    {
        if (!GameSceneManager.Instance) return;

        Color color = GameSceneManager.Instance.GetCharacterColor(CharacterIndex);

        foreach (Renderer currentRenderer in renderers)
        {
            if (!currentRenderer) continue;

            currentRenderer.material.color = color;
        }
    }
}
