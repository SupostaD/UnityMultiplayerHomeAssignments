using Fusion;
using UnityEngine;

public class LobbyPlayerInfo : NetworkBehaviour
{
    [Networked] public PlayerRef Player { get; set; }
    [Networked] public NetworkString<_32> PlayerName { get; set; }

    public override void Spawned()
    {
        LobbyManager.Instance?.RequestPlayersListRefresh();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        LobbyManager.Instance?.RequestPlayersListRefresh();
    }
}
