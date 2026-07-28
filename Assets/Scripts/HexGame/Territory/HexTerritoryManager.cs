using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class HexTerritoryManager : NetworkBehaviour
{
    public const int MaximumNetworkedTiles = 512;
    public const int MaximumNetworkedPlayers = 16;
    private const float MinimumStrength = 0.0001f;

    private enum CaptureVisualMode
    {
        Immediate = 0,
        PlayerImpact = 1,
        HexCorners = 2
    }

    [Header("Required References")]
    [SerializeField] private HexMapGenerator mapGenerator;
    [SerializeField] private HexTerritoryCaptureSettings captureSettings;

    [Header("Capture Validation")]
    [SerializeField, Min(1f)] private float maximumCaptureDistanceInTileRadii = 1.5f;

    [Header("Visuals")]
    [SerializeField] private Color unclaimedColor = Color.white;
    
    [Header("Death Zone VFX")]
    [SerializeField] private BallFallVFX fallVFX;
    [SerializeField] private float fallVFXHeightOffset = 0.75f;

    [Networked]
    public int ActiveTileCount { get; private set; }

    [Networked, Capacity(MaximumNetworkedTiles)]
    private NetworkArray<int> TileOwnerKeys => default;

    [Networked, Capacity(MaximumNetworkedTiles)]
    private NetworkArray<int> TileCaptureVisualModes => default;

    [Networked, Capacity(MaximumNetworkedTiles)]
    private NetworkArray<Vector2> TileCaptureImpactPoints => default;

    [Networked, Capacity(MaximumNetworkedPlayers)]
    private NetworkArray<float> PlayerStrengths => default;

    [Networked, Capacity(MaximumNetworkedPlayers)]
    private NetworkArray<int> PlayerTerritoryCounts => default;

    [Networked, Capacity(MaximumNetworkedPlayers)]
    private NetworkArray<NetworkBool> PlayerHasEnteredMatch => default;

    [Networked, Capacity(MaximumNetworkedPlayers)]
    private NetworkArray<NetworkBool> EliminatedPlayers => default;

    private readonly int[] renderedOwnerKeys = new int[MaximumNetworkedTiles];
    private readonly int[] renderedCharacterIndices = new int[MaximumNetworkedTiles];
    private readonly int[] predictedOwnerKeys = new int[MaximumNetworkedTiles];
    private readonly bool[] rejectedPredictedCaptures =
        new bool[MaximumNetworkedTiles];
    private readonly float[] predictedCaptureExpiryTimes =
        new float[MaximumNetworkedTiles];
    private readonly bool[] exteriorReachableTiles =
        new bool[MaximumNetworkedTiles];
    private readonly Queue<int> exteriorFloodQueue =
        new Queue<int>(MaximumNetworkedTiles);
    private readonly List<int> enclosedTileIndices =
        new List<int>(MaximumNetworkedTiles);
    private readonly int[] enclosedStrengthLosses =
        new int[MaximumNetworkedPlayers];
    private bool visualsInitialized;

    public override void Spawned()
    {
        if (!ValidateMap())
            return;

        if (Object.HasStateAuthority)
        {
            ActiveTileCount = mapGenerator.GeneratedTileCount;

            for (int i = 0; i < ActiveTileCount; i++)
            {
                SetTileOwner(
                    i,
                    0,
                    CaptureVisualMode.Immediate,
                    Vector2.zero
                );
            }

            for (int i = 0; i < MaximumNetworkedPlayers; i++)
            {
                PlayerStrengths.Set(i, 0f);
                PlayerTerritoryCounts.Set(i, 0);
                PlayerHasEnteredMatch.Set(i, false);
                EliminatedPlayers.Set(i, false);
            }
        }

        ResetVisualCache();
        RefreshTileVisuals(false);
    }

    public override void Render()
    {
        RefreshTileVisuals(true);
    }

    public bool TryGetCoordinate(
        Vector3 worldPosition,
        out HexCoord coordinate)
    {
        coordinate = default;

        return mapGenerator != null &&
               mapGenerator.TryWorldToCoordinate(worldPosition, out coordinate);
    }

    public bool TryGetTileTopCenter(
        HexCoord coordinate,
        out Vector3 worldTopCenter)
    {
        worldTopCenter = Vector3.zero;

        if (mapGenerator == null ||
            !mapGenerator.TryGetTile(
                coordinate,
                out HexTile tile) ||
            tile == null)
        {
            return false;
        }

        worldTopCenter = tile.WorldTopCenter;
        return true;
    }

    public bool IsOwnedBy(HexCoord coordinate, PlayerRef player)
    {
        if (mapGenerator == null ||
            !mapGenerator.TryGetCoordinateIndex(coordinate, out int tileIndex) ||
            tileIndex < 0 ||
            tileIndex >= GetUsableTileCount())
        {
            return false;
        }

        return TileOwnerKeys[tileIndex] == EncodeOwner(player);
    }

    public float GetStrength(PlayerRef player)
    {
        return TryGetPlayerSlot(player, out int slot)
            ? Mathf.Max(0f, PlayerStrengths[slot])
            : 0f;
    }

    public float GetDisplayedStrength(PlayerRef player)
    {
        float strength = GetStrength(player);

        return strength <= MinimumStrength
            ? 0f
            : strength;
    }

    public int GetTerritoryCount(PlayerRef player)
    {
        return TryGetPlayerSlot(player, out int slot)
            ? Mathf.Max(0, PlayerTerritoryCounts[slot])
            : 0;
    }

    public bool IsPlayerEliminated(PlayerRef player)
    {
        return TryGetPlayerSlot(player, out int slot) &&
               EliminatedPlayers[slot];
    }

    public bool TryTransferStrength(
        PlayerRef firstPlayer,
        PlayerRef secondPlayer,
        float requestedAmount)
    {
        if (Object == null ||
            !Object.HasStateAuthority ||
            firstPlayer == secondPlayer ||
            requestedAmount <= 0f ||
            !TryGetPlayerSlot(firstPlayer, out int firstSlot) ||
            !TryGetPlayerSlot(secondPlayer, out int secondSlot) ||
            EliminatedPlayers[firstSlot] ||
            EliminatedPlayers[secondSlot] ||
            !PlayerHasEnteredMatch[firstSlot] ||
            !PlayerHasEnteredMatch[secondSlot])
        {
            return false;
        }

        float firstStrength = Mathf.Max(0f, PlayerStrengths[firstSlot]);
        float secondStrength = Mathf.Max(0f, PlayerStrengths[secondSlot]);

        if (Mathf.Approximately(firstStrength, secondStrength))
            return false;

        int strongerSlot = firstStrength > secondStrength
            ? firstSlot
            : secondSlot;
        int weakerSlot = firstStrength > secondStrength
            ? secondSlot
            : firstSlot;
        float transferableAmount = Mathf.Min(
            Mathf.Max(0f, requestedAmount),
            PlayerStrengths[weakerSlot]
        );

        if (transferableAmount <= 0f)
        {
            EliminatePlayer(weakerSlot);
            return false;
        }

        PlayerStrengths.Set(
            strongerSlot,
            PlayerStrengths[strongerSlot] + transferableAmount
        );
        PlayerStrengths.Set(
            weakerSlot,
            Mathf.Max(0f, PlayerStrengths[weakerSlot] - transferableAmount)
        );

        if (PlayerStrengths[weakerSlot] <= MinimumStrength)
            EliminatePlayer(weakerSlot);

        return true;
    }

    public void RequestCapture(
        HexCoord coordinate,
        Vector3 reportedPlayerPosition,
        bool reportedGrounded)
    {
        if (Runner == null || Object == null)
            return;

        PredictLocalCapture(
            coordinate,
            reportedPlayerPosition
        );
        RPC_RequestCapture(
            coordinate.q,
            coordinate.r,
            coordinate.layer,
            reportedPlayerPosition,
            reportedGrounded
        );
    }

    public void RequestDeathZonePenalty()
    {
        if (Runner == null || Object == null)
            return;

        RPC_RequestDeathZonePenalty();
    }

    public void ReleasePlayerTerritory(PlayerRef player)
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        int ownerKey = EncodeOwner(player);
        int tileCount = GetUsableTileCount();

        for (int i = 0; i < tileCount; i++)
        {
            if (TileOwnerKeys[i] == ownerKey)
            {
                SetTileOwner(
                    i,
                    0,
                    CaptureVisualMode.Immediate,
                    Vector2.zero
                );
            }
        }

        if (TryGetPlayerSlot(player, out int slot))
        {
            PlayerStrengths.Set(slot, 0f);
            PlayerTerritoryCounts.Set(slot, 0);
            PlayerHasEnteredMatch.Set(slot, false);
            EliminatedPlayers.Set(slot, false);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestCapture(
        int q,
        int r,
        int layer,
        Vector3 reportedPlayerPosition,
        bool reportedGrounded,
        RpcInfo info = default)
    {
        PlayerRef requestingPlayer = info.Source;

        if (requestingPlayer == PlayerRef.None)
            return;

        HexCoord coordinate = new HexCoord(q, r, layer);

        if (!TryValidateCapture(
                requestingPlayer,
                coordinate,
                reportedPlayerPosition,
                reportedGrounded,
                out int tileIndex,
                out Vector3 playerWorldPosition))
        {
            RPC_CaptureRejected(
                requestingPlayer,
                q,
                r,
                layer
            );
            return;
        }

        int newOwnerKey = EncodeOwner(requestingPlayer);
        int previousOwnerKey = TileOwnerKeys[tileIndex];

        if (previousOwnerKey == newOwnerKey)
            return;

        if (IsContestedCapture(
                requestingPlayer,
                coordinate,
                previousOwnerKey))
        {
            NeutralizeContestedTile(tileIndex, previousOwnerKey);
            RPC_CaptureRejected(
                requestingPlayer,
                q,
                r,
                layer
            );
            return;
        }

        if (!mapGenerator.TryGetTile(coordinate, out HexTile capturedTile) ||
            capturedTile == null)
        {
            RPC_CaptureRejected(
                requestingPlayer,
                q,
                r,
                layer
            );
            return;
        }

        SetTileOwner(
            tileIndex,
            newOwnerKey,
            CaptureVisualMode.PlayerImpact,
            capturedTile.WorldToCapturePoint(playerWorldPosition)
        );

        if (previousOwnerKey != 0 &&
            TryGetPlayerByOwnerKey(previousOwnerKey, out PlayerRef previousOwner))
        {
            UpdateTerritoryCount(previousOwner);

            if (GetGameRulesSettings().LoseStrengthWhenHexIsStolen)
                ApplyStrengthDelta(previousOwner, -1f);
        }

        if (!TryGetPlayerSlot(requestingPlayer, out int requestingPlayerSlot))
        {
            return;
        }

        PlayerHasEnteredMatch.Set(requestingPlayerSlot, true);
        ApplyStrengthDelta(requestingPlayer, 1f);
        CaptureEnclosedArea(requestingPlayer, newOwnerKey);
        UpdateTerritoryCount(requestingPlayer);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_CaptureRejected(
        [RpcTarget] PlayerRef targetPlayer,
        int q,
        int r,
        int layer)
    {
        if (Runner == null ||
            Runner.LocalPlayer != targetPlayer ||
            mapGenerator == null ||
            !mapGenerator.TryGetCoordinateIndex(
                new HexCoord(q, r, layer),
                out int tileIndex) ||
            tileIndex < 0 ||
            tileIndex >= MaximumNetworkedTiles)
        {
            return;
        }

        rejectedPredictedCaptures[tileIndex] = true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDeathZonePenalty(
        RpcInfo info = default)
    {
        PlayerRef affectedPlayer = info.Source;

        if (affectedPlayer == PlayerRef.None ||
            !TryGetPlayerSlot(affectedPlayer, out int playerSlot) ||
            EliminatedPlayers[playerSlot])
        {
            return;
        }
        
        NetworkObject affectedPlayerObject = Runner.GetPlayerObject(affectedPlayer);

        if (affectedPlayerObject != null)
        {
            Vector3 vfxPosition =
                affectedPlayerObject.transform.position +
                Vector3.up * fallVFXHeightOffset;

            RPC_PlayDeathZoneFallVFX(vfxPosition);
        }

        HexGameRulesSettings rules = GetGameRulesSettings();

        if (rules == null)
            return;

        float currentStrength =
            Mathf.Max(0f, PlayerStrengths[playerSlot]);

        if (currentStrength <=
            rules.DeathZoneProtectedStrengthThreshold)
        {
            RPC_ReturnPlayerFromDeathZone(affectedPlayer);
            return;
        }

        float strengthLoss =
            currentStrength *
            rules.DeathZoneStrengthLossFraction;

        if (rules.RoundDeathZoneStrengthLossUp)
            strengthLoss = Mathf.Ceil(strengthLoss);

        strengthLoss = Mathf.Min(currentStrength, strengthLoss);

        if (strengthLoss > 0f)
            ApplyStrengthDelta(playerSlot, -strengthLoss);

        RPC_ReturnPlayerFromDeathZone(affectedPlayer);
    }
    
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayDeathZoneFallVFX(Vector3 worldPosition)
    {
        fallVFX?.PlayAbyssExplosion(worldPosition);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ReturnPlayerFromDeathZone(
        [RpcTarget] PlayerRef targetPlayer)
    {
        if (Runner == null ||
            Runner.LocalPlayer != targetPlayer)
        {
            return;
        }

        NetworkObject playerObject =
            Runner.GetPlayerObject(targetPlayer);

        if (NetworkObjectBehaviourReferences.TryGet(
                playerObject,
                out HexBallPlayerController playerController))
        {
            playerController.ReturnToLastSafeHex();
        }
    }

    private bool TryValidateCapture(
        PlayerRef requestingPlayer,
        HexCoord coordinate,
        Vector3 reportedPlayerPosition,
        bool reportedGrounded,
        out int tileIndex,
        out Vector3 playerWorldPosition)
    {
        tileIndex = -1;
        playerWorldPosition = Vector3.zero;

        if (!ValidateMap() ||
            !mapGenerator.TryGetCoordinateIndex(coordinate, out tileIndex) ||
            tileIndex < 0 ||
            tileIndex >= GetUsableTileCount())
        {
            return false;
        }

        if (IsPlayerEliminated(requestingPlayer) ||
            !reportedGrounded)
            return false;

        NetworkObject playerObject = Runner.GetPlayerObject(requestingPlayer);

        if (!NetworkObjectBehaviourReferences.TryGet(
                playerObject,
                out HexBallPlayerController playerController) ||
            !mapGenerator.TryWorldToCoordinate(
                reportedPlayerPosition,
                out HexCoord validatedCoordinate) ||
            validatedCoordinate != coordinate)
        {
            return false;
        }

        Vector3 tileCenter = mapGenerator.CoordinateToWorldPosition(coordinate);
        Vector3 playerCenter = reportedPlayerPosition;
        playerWorldPosition = playerCenter;
        Vector2 planarDifference = new Vector2(
            playerCenter.x - tileCenter.x,
            playerCenter.z - tileCenter.z
        );
        float maximumDistance =
            mapGenerator.TileOuterRadius *
            Mathf.Max(1f, maximumCaptureDistanceInTileRadii);

        if (planarDifference.sqrMagnitude >
            maximumDistance * maximumDistance)
        {
            return false;
        }

        Vector3 latestProxyPosition =
            playerObject.transform.position;
        Vector2 reportedDrift = new Vector2(
            playerCenter.x - latestProxyPosition.x,
            playerCenter.z - latestProxyPosition.z
        );
        float maximumReportedDrift =
            captureSettings.MaximumReportedPositionDrift;

        return reportedDrift.sqrMagnitude <=
               maximumReportedDrift * maximumReportedDrift;
    }

    private bool IsContestedCapture(
        PlayerRef requestingPlayer,
        HexCoord coordinate,
        int currentOwnerKey)
    {
        if (currentOwnerKey != 0)
        {
            return TryGetPlayerByOwnerKey(
                       currentOwnerKey,
                       out PlayerRef currentOwner) &&
                   currentOwner != requestingPlayer &&
                   IsPlayerStandingOnCoordinate(
                       currentOwner,
                       coordinate
                   );
        }

        foreach (PlayerRef otherPlayer in Runner.ActivePlayers)
        {
            if (otherPlayer == requestingPlayer ||
                IsPlayerEliminated(otherPlayer))
            {
                continue;
            }

            if (IsPlayerStandingOnCoordinate(otherPlayer, coordinate))
                return true;
        }

        return false;
    }

    private bool IsPlayerStandingOnCoordinate(
        PlayerRef player,
        HexCoord coordinate)
    {
        NetworkObject playerObject = Runner.GetPlayerObject(player);

        if (!NetworkObjectBehaviourReferences.TryGet(
                playerObject,
                out HexBallPlayerController playerController) ||
            !playerController.IsTouchingGroundForCapture() ||
            !mapGenerator.TryWorldToCoordinate(
                playerObject.transform.position,
                out HexCoord playerCoordinate))
        {
            return false;
        }

        return playerCoordinate == coordinate;
    }

    private void NeutralizeContestedTile(
        int tileIndex,
        int previousOwnerKey)
    {
        if (tileIndex < 0 ||
            tileIndex >= GetUsableTileCount() ||
            TileOwnerKeys[tileIndex] == 0)
        {
            return;
        }

        SetTileOwner(
            tileIndex,
            0,
            CaptureVisualMode.Immediate,
            Vector2.zero
        );

        int previousOwnerSlot = previousOwnerKey - 1;

        if (previousOwnerSlot >= 0 &&
            previousOwnerSlot < MaximumNetworkedPlayers)
        {
            UpdateTerritoryCount(previousOwnerSlot);
        }
    }

    private void CaptureEnclosedArea(
        PlayerRef capturingPlayer,
        int capturingOwnerKey)
    {
        FindEnclosedTiles(capturingOwnerKey);

        if (enclosedTileIndices.Count == 0)
            return;

        for (int slot = 0; slot < MaximumNetworkedPlayers; slot++)
            enclosedStrengthLosses[slot] = 0;

        int capturedTileCount = 0;

        foreach (int tileIndex in enclosedTileIndices)
        {
            int previousOwnerKey = TileOwnerKeys[tileIndex];

            if (previousOwnerKey == capturingOwnerKey)
                continue;

            SetTileOwner(
                tileIndex,
                capturingOwnerKey,
                CaptureVisualMode.HexCorners,
                Vector2.zero
            );
            capturedTileCount++;

            int previousOwnerSlot = previousOwnerKey - 1;

            if (previousOwnerSlot >= 0 &&
                previousOwnerSlot < MaximumNetworkedPlayers)
            {
                enclosedStrengthLosses[previousOwnerSlot]++;
            }
        }

        if (capturedTileCount == 0)
            return;

        for (int slot = 0; slot < MaximumNetworkedPlayers; slot++)
        {
            int lostTileCount = enclosedStrengthLosses[slot];

            if (lostTileCount <= 0)
                continue;

            UpdateTerritoryCount(slot);

            if (GetGameRulesSettings().LoseStrengthWhenHexIsStolen)
                ApplyStrengthDelta(slot, -lostTileCount);
        }

        float strengthReward =
            capturedTileCount *
            captureSettings.EnclosedHexStrengthMultiplier;

        ApplyStrengthDelta(capturingPlayer, strengthReward);
    }

    private void FindEnclosedTiles(int ownerKey)
    {
        int tileCount = GetUsableTileCount();
        exteriorFloodQueue.Clear();
        enclosedTileIndices.Clear();

        for (int i = 0; i < tileCount; i++)
            exteriorReachableTiles[i] = false;

        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            if (TileOwnerKeys[tileIndex] == ownerKey ||
                !mapGenerator.TryGetCoordinateAtIndex(
                    tileIndex,
                    out HexCoord coordinate) ||
                !IsMapBoundaryTile(coordinate, tileCount))
            {
                continue;
            }

            exteriorReachableTiles[tileIndex] = true;
            exteriorFloodQueue.Enqueue(tileIndex);
        }

        while (exteriorFloodQueue.Count > 0)
        {
            int currentIndex = exteriorFloodQueue.Dequeue();

            if (!mapGenerator.TryGetCoordinateAtIndex(
                    currentIndex,
                    out HexCoord currentCoordinate))
            {
                continue;
            }

            for (int direction = 0; direction < 6; direction++)
            {
                HexCoord neighborCoordinate =
                    currentCoordinate.GetNeighbor(direction);

                if (!mapGenerator.TryGetCoordinateIndex(
                        neighborCoordinate,
                        out int neighborIndex) ||
                    neighborIndex < 0 ||
                    neighborIndex >= tileCount ||
                    exteriorReachableTiles[neighborIndex] ||
                    TileOwnerKeys[neighborIndex] == ownerKey)
                {
                    continue;
                }

                exteriorReachableTiles[neighborIndex] = true;
                exteriorFloodQueue.Enqueue(neighborIndex);
            }
        }

        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            if (TileOwnerKeys[tileIndex] != ownerKey &&
                !exteriorReachableTiles[tileIndex])
            {
                enclosedTileIndices.Add(tileIndex);
            }
        }
    }

    private bool IsMapBoundaryTile(
        HexCoord coordinate,
        int tileCount)
    {
        for (int direction = 0; direction < 6; direction++)
        {
            if (!mapGenerator.TryGetCoordinateIndex(
                    coordinate.GetNeighbor(direction),
                    out int neighborIndex) ||
                neighborIndex < 0 ||
                neighborIndex >= tileCount)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateTerritoryCount(PlayerRef player)
    {
        if (!TryGetPlayerSlot(player, out int slot))
            return;

        UpdateTerritoryCount(slot);
    }

    private void UpdateTerritoryCount(int slot)
    {
        if (slot < 0 || slot >= MaximumNetworkedPlayers)
            return;

        PlayerTerritoryCounts.Set(
            slot,
            CountOwnedTiles(slot + 1)
        );
    }

    private void ApplyStrengthDelta(PlayerRef player, float delta)
    {
        if (!TryGetPlayerSlot(player, out int slot))
            return;

        ApplyStrengthDelta(slot, delta);
    }

    private void ApplyStrengthDelta(int slot, float delta)
    {
        if (slot < 0 ||
            slot >= MaximumNetworkedPlayers ||
            EliminatedPlayers[slot])
        {
            return;
        }

        float newStrength = Mathf.Max(0f, PlayerStrengths[slot] + delta);
        PlayerStrengths.Set(slot, newStrength);

        if (delta < 0f &&
            PlayerHasEnteredMatch[slot] &&
            newStrength <= MinimumStrength)
        {
            EliminatePlayer(slot);
        }
    }

    private void EliminatePlayer(int playerSlot)
    {
        if (playerSlot < 0 ||
            playerSlot >= MaximumNetworkedPlayers ||
            EliminatedPlayers[playerSlot])
        {
            return;
        }

        PlayerStrengths.Set(playerSlot, 0f);
        PlayerTerritoryCounts.Set(playerSlot, 0);
        EliminatedPlayers.Set(playerSlot, true);

        int ownerKey = playerSlot + 1;
        int tileCount = GetUsableTileCount();

        for (int i = 0; i < tileCount; i++)
        {
            if (TileOwnerKeys[i] == ownerKey)
            {
                SetTileOwner(
                    i,
                    0,
                    CaptureVisualMode.Immediate,
                    Vector2.zero
                );
            }
        }
    }

    private int CountOwnedTiles(int ownerKey)
    {
        int result = 0;
        int tileCount = GetUsableTileCount();

        for (int i = 0; i < tileCount; i++)
        {
            if (TileOwnerKeys[i] == ownerKey)
                result++;
        }

        return result;
    }

    private bool TryGetPlayerByOwnerKey(
        int ownerKey,
        out PlayerRef result)
    {
        result = PlayerRef.None;

        if (Runner == null || ownerKey == 0)
            return false;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (EncodeOwner(player) != ownerKey)
                continue;

            result = player;
            return true;
        }

        return false;
    }

    private int GetCharacterIndex(int ownerKey)
    {
        if (!TryGetPlayerByOwnerKey(ownerKey, out PlayerRef player))
            return -1;

        NetworkObject playerObject = Runner.GetPlayerObject(player);

        return NetworkObjectBehaviourReferences.TryGet(
            playerObject,
            out NetworkPlayerCharacter playerCharacter)
                ? playerCharacter.CharacterIndex
                : -1;
    }

    private void RefreshTileVisuals(bool animateChanges)
    {
        if (mapGenerator == null || mapGenerator.Board == null)
            return;

        int tileCount = GetUsableTileCount();

        for (int i = 0; i < tileCount; i++)
        {
            int ownerKey = TileOwnerKeys[i];
            int characterIndex = ownerKey == 0
                ? -1
                : GetCharacterIndex(ownerKey);
            bool forceImmediateVisual = false;

            if (predictedOwnerKeys[i] != 0)
            {
                if (ownerKey == predictedOwnerKeys[i])
                {
                    ClearPredictedCapture(i);
                    renderedOwnerKeys[i] = ownerKey;
                    renderedCharacterIndices[i] = characterIndex;
                    continue;
                }

                bool predictionExpired =
                    Time.unscaledTime >=
                    predictedCaptureExpiryTimes[i];

                if (!rejectedPredictedCaptures[i] &&
                    !predictionExpired)
                {
                    continue;
                }

                ClearPredictedCapture(i);
                renderedOwnerKeys[i] = int.MinValue;
                renderedCharacterIndices[i] = int.MinValue;
                forceImmediateVisual = true;
            }

            if (visualsInitialized &&
                renderedOwnerKeys[i] == ownerKey &&
                renderedCharacterIndices[i] == characterIndex)
            {
                continue;
            }

            if (!visualsInitialized && ownerKey == 0)
            {
                renderedOwnerKeys[i] = ownerKey;
                renderedCharacterIndices[i] = characterIndex;
                continue;
            }

            if (!mapGenerator.TryGetCoordinateAtIndex(i, out HexCoord coordinate) ||
                !mapGenerator.Board.TryGetTile(coordinate, out HexTile tile) ||
                tile == null)
            {
                continue;
            }

            Color tileColor = ownerKey == 0
                ? unclaimedColor
                : GetPlayerColor(characterIndex);

            tile.ConfigureCaptureVisual(captureSettings);

            bool ownerChanged =
                renderedOwnerKeys[i] != ownerKey;

            if (forceImmediateVisual ||
                !animateChanges ||
                !visualsInitialized ||
                !ownerChanged ||
                ownerKey == 0)
            {
                tile.SetColor(tileColor);
            }
            else
            {
                CaptureVisualMode visualMode =
                    (CaptureVisualMode)TileCaptureVisualModes[i];

                if (visualMode == CaptureVisualMode.HexCorners)
                {
                    tile.CaptureFromCorners(tileColor);
                }
                else if (visualMode == CaptureVisualMode.PlayerImpact)
                {
                    tile.CaptureFromPlayer(
                        tileColor,
                        TileCaptureImpactPoints[i]
                    );
                }
                else
                {
                    tile.SetColor(tileColor);
                }
            }

            renderedOwnerKeys[i] = ownerKey;
            renderedCharacterIndices[i] = characterIndex;
        }

        visualsInitialized = true;
    }

    private void PredictLocalCapture(
        HexCoord coordinate,
        Vector3 reportedPlayerPosition)
    {
        if (Runner == null ||
            captureSettings == null ||
            mapGenerator == null ||
            Runner.LocalPlayer == PlayerRef.None ||
            !mapGenerator.TryGetCoordinateIndex(
                coordinate,
                out int tileIndex) ||
            tileIndex < 0 ||
            tileIndex >= GetUsableTileCount())
        {
            return;
        }

        int predictedOwnerKey =
            EncodeOwner(Runner.LocalPlayer);

        if (predictedOwnerKeys[tileIndex] == predictedOwnerKey ||
            TileOwnerKeys[tileIndex] == predictedOwnerKey ||
            !mapGenerator.TryGetTile(
                coordinate,
                out HexTile tile) ||
            tile == null)
        {
            return;
        }

        int characterIndex =
            GetCharacterIndex(predictedOwnerKey);
        Color predictedColor =
            GetPlayerColor(characterIndex);

        tile.ConfigureCaptureVisual(captureSettings);
        tile.CaptureFromPlayer(
            predictedColor,
            tile.WorldToCapturePoint(reportedPlayerPosition)
        );

        predictedOwnerKeys[tileIndex] = predictedOwnerKey;
        rejectedPredictedCaptures[tileIndex] = false;
        predictedCaptureExpiryTimes[tileIndex] =
            Time.unscaledTime +
            captureSettings.LocalPredictionTimeoutSeconds;
    }

    private void ClearPredictedCapture(int tileIndex)
    {
        if (tileIndex < 0 ||
            tileIndex >= MaximumNetworkedTiles)
        {
            return;
        }

        predictedOwnerKeys[tileIndex] = 0;
        rejectedPredictedCaptures[tileIndex] = false;
        predictedCaptureExpiryTimes[tileIndex] = 0f;
    }

    private void SetTileOwner(
        int tileIndex,
        int ownerKey,
        CaptureVisualMode visualMode,
        Vector2 localImpactPoint)
    {
        TileCaptureVisualModes.Set(tileIndex, (int)visualMode);
        TileCaptureImpactPoints.Set(tileIndex, localImpactPoint);
        TileOwnerKeys.Set(tileIndex, ownerKey);
    }

    private Color GetPlayerColor(int characterIndex)
    {
        return GameSceneManager.Instance != null && characterIndex >= 0
            ? GameSceneManager.Instance.GetCharacterColor(characterIndex)
            : Color.white;
    }

    private int GetUsableTileCount()
    {
        if (mapGenerator == null)
            return 0;

        int generatedCount = Mathf.Min(
            mapGenerator.GeneratedTileCount,
            MaximumNetworkedTiles
        );

        return ActiveTileCount > 0
            ? Mathf.Min(generatedCount, ActiveTileCount)
            : generatedCount;
    }

    private void ResetVisualCache()
    {
        visualsInitialized = false;

        for (int i = 0; i < MaximumNetworkedTiles; i++)
        {
            renderedOwnerKeys[i] = int.MinValue;
            renderedCharacterIndices[i] = int.MinValue;
            ClearPredictedCapture(i);
        }
    }

    private bool ValidateMap()
    {
        if (mapGenerator == null)
        {
            Debug.LogError(
                "HexTerritoryManager: Map Generator is not assigned.",
                this
            );
            return false;
        }

        if (captureSettings == null)
        {
            Debug.LogError(
                "HexTerritoryManager: Capture Settings is not assigned.",
                this
            );
            return false;
        }

        if (GetGameRulesSettings() == null)
        {
            Debug.LogError(
                "HexTerritoryManager: Game Rules Settings is not assigned on GameSceneManager.",
                this
            );
            return false;
        }

        if (mapGenerator.GeneratedTileCount > MaximumNetworkedTiles)
        {
            Debug.LogError(
                $"HexTerritoryManager supports at most {MaximumNetworkedTiles} tiles, " +
                $"but the map contains {mapGenerator.GeneratedTileCount}.",
                this
            );
            return false;
        }

        return true;
    }

    private static HexGameRulesSettings GetGameRulesSettings()
    {
        return GameSceneManager.Instance != null
            ? GameSceneManager.Instance.GameRulesSettings
            : null;
    }

    private static bool TryGetPlayerSlot(
        PlayerRef player,
        out int slot)
    {
        slot = player.PlayerId;

        return player != PlayerRef.None &&
               slot >= 0 &&
               slot < MaximumNetworkedPlayers;
    }

    private static int EncodeOwner(PlayerRef player)
    {
        return player == PlayerRef.None ? 0 : player.PlayerId + 1;
    }
}
