using System;
using Fusion;
using UnityEngine;

[Serializable]
public struct HexAbilitySpawnPoint
{
    public HexCoord tileCoordinate;
    public HexPlayerAbilityType abilityType;
    [Min(0f)] public float heightAboveTile;
}

[DisallowMultipleComponent]
public sealed class HexAbilityPickupSpawner : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private HexMapGenerator mapGenerator;
    [SerializeField] private NetworkObject dashPickupPrefab;
    [SerializeField] private NetworkObject doubleJumpPickupPrefab;

    [Header("Spawn Points")]
    [SerializeField] private HexAbilitySpawnPoint[] spawnPoints;

    [Networked]
    private NetworkBool PickupsSpawned { get; set; }

    private void Awake()
    {
        ValidateReferences();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority ||
            PickupsSpawned)
        {
            return;
        }

        GameSceneManager sceneManager =
            GameSceneManager.Instance;

        if (sceneManager == null ||
            !sceneManager.IsMatchStarted ||
            sceneManager.IsGameEnded)
        {
            return;
        }

        PickupsSpawned = true;
        SpawnAllPickups();
    }

    private void SpawnAllPickups()
    {
        if (mapGenerator == null ||
            spawnPoints == null)
        {
            return;
        }

        foreach (HexAbilitySpawnPoint spawnPoint in spawnPoints)
        {
            if (!mapGenerator.TryGetTile(
                    spawnPoint.tileCoordinate,
                    out HexTile tile))
            {
                Debug.LogError(
                    $"HexAbilityPickupSpawner: Tile {spawnPoint.tileCoordinate} was not found.",
                    this
                );
                continue;
            }

            NetworkObject pickupPrefab =
                GetPickupPrefab(spawnPoint.abilityType);

            if (pickupPrefab == null)
            {
                Debug.LogError(
                    $"HexAbilityPickupSpawner: No prefab is assigned for {spawnPoint.abilityType}.",
                    this
                );
                continue;
            }

            Vector3 spawnPosition =
                tile.WorldTopCenter +
                Vector3.up *
                Mathf.Max(0f, spawnPoint.heightAboveTile);

            Runner.Spawn(
                pickupPrefab,
                spawnPosition,
                Quaternion.identity,
                null,
                (spawnRunner, spawnedObject) =>
                {
                    if (NetworkObjectBehaviourReferences.TryGet(
                            spawnedObject,
                            out HexAbilityPickup pickup))
                    {
                        pickup.InitializeSpawnPosition(
                            spawnPosition
                        );
                    }
                }
            );
        }
    }

    private NetworkObject GetPickupPrefab(
        HexPlayerAbilityType abilityType)
    {
        switch (abilityType)
        {
            case HexPlayerAbilityType.Dash:
                return dashPickupPrefab;

            case HexPlayerAbilityType.DoubleJump:
                return doubleJumpPickupPrefab;

            default:
                return null;
        }
    }

    private void ValidateReferences()
    {
        if (mapGenerator == null)
        {
            Debug.LogError(
                "HexAbilityPickupSpawner: Map Generator is not assigned.",
                this
            );
        }

        if (dashPickupPrefab == null)
        {
            Debug.LogError(
                "HexAbilityPickupSpawner: Dash Pickup Prefab is not assigned.",
                this
            );
        }

        if (doubleJumpPickupPrefab == null)
        {
            Debug.LogError(
                "HexAbilityPickupSpawner: Double Jump Pickup Prefab is not assigned.",
                this
            );
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError(
                "HexAbilityPickupSpawner: Spawn Points are empty.",
                this
            );
        }
    }
}
