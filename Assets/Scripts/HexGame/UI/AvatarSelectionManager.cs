using Fusion;
using UnityEngine;

public class AvatarSelectionManager : NetworkBehaviour
{
    private const int MaxAvatars = 10;

    [Header("UI")]
    [SerializeField] private AvatarSelectionUI selectionUI;

    [Networked, OnChangedRender(nameof(OnOccupiedAvatarMaskChanged))] 
    public int OccupiedAvatarMask { get; private set; }

    [Networked, Capacity(MaxAvatars)]
    private NetworkArray<PlayerRef> AvatarOwners => default;

    public int LocalAvatarIndex { get; private set; } = -1;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            OccupiedAvatarMask = 0;
            for (int i = 0; i < MaxAvatars; i++) 
                AvatarOwners.Set(i, PlayerRef.None);
        }

        if (selectionUI)
        {
            selectionUI.Init(this);
            selectionUI.Refresh(OccupiedAvatarMask, LocalAvatarIndex);
            selectionUI.Show();
        }
    }

    public void RequestAvatar(int avatarIndex)
    {
        if (!IsValidAvatarIndex(avatarIndex))
        {
            selectionUI?.SetStatus("Invalid avatar.");
            return;
        }

        if (LocalAvatarIndex >= 0)
        {
            selectionUI?.SetStatus("You already selected an avatar.");
            return;
        }

        selectionUI?.SetStatus("Requesting avatar...");

        RPC_RequestAvatar(avatarIndex);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestAvatar(int avatarIndex, RpcInfo info = default)
    {
        PlayerRef requestingPlayer = info.Source;

        if (requestingPlayer == PlayerRef.None)
            return;

        if (GameSceneManager.Instance && GameSceneManager.Instance.IsMatchStarted)
        {
            RPC_AvatarRejected(requestingPlayer, "The match has already started.");
            return;
        }

        if (!IsValidAvatarIndex(avatarIndex))
        {
            RPC_AvatarRejected(requestingPlayer, "Invalid avatar.");
            return;
        }

        if (FindAvatarForPlayer(requestingPlayer) >= 0)
        {
            RPC_AvatarRejected(requestingPlayer, "You already selected an avatar.");
            return;
        }

        if (AvatarOwners[avatarIndex] != PlayerRef.None)
        {
            RPC_AvatarRejected(requestingPlayer, "This avatar is already taken.");
            return;
        }

        AvatarOwners.Set(avatarIndex, requestingPlayer);

        OccupiedAvatarMask |= 1 << avatarIndex;

        RPC_AvatarApproved(requestingPlayer, avatarIndex);

        GameSceneManager.Instance?.NotifyAvatarSelectionChanged();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AvatarApproved([RpcTarget] PlayerRef targetPlayer, int avatarIndex)
    {
        if (Runner.LocalPlayer != targetPlayer)
            return;

        LocalAvatarIndex = avatarIndex;

        selectionUI?.Refresh(OccupiedAvatarMask, LocalAvatarIndex);

        selectionUI?.SetStatus($"Selected: {GetAvatarName(avatarIndex)}");

        TryApplyLocalAvatar();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AvatarRejected([RpcTarget] PlayerRef targetPlayer, string reason)
    {
        if (Runner.LocalPlayer != targetPlayer)
            return;
        
        selectionUI?.SetStatus(reason);
    }

    public void ApplyLocalSelectionToPlayer(NetworkPlayerCharacter playerCharacter)
    {
        if (LocalAvatarIndex < 0 || !playerCharacter ||
            !playerCharacter.Object || !playerCharacter.Object.HasStateAuthority) 
            return;

        playerCharacter.AvatarIndex = LocalAvatarIndex;
    }

    public bool AllActivePlayersHaveAvatar()
    {
        if (!Runner) return false;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (FindAvatarForPlayer(player) < 0)
                return false;
        }

        return true;
    }

    public void ReleaseAvatar(PlayerRef player)
    {
        if (!Object || !Object.HasStateAuthority)
            return;

        for (int i = 0; i < MaxAvatars; i++)
        {
            if (AvatarOwners[i] != player) continue;

            AvatarOwners.Set(i, PlayerRef.None);

            OccupiedAvatarMask &= ~(1 << i);
        }
    }

    public void HideSelectionUI()
    {
        selectionUI?.Hide();
    }

    private void TryApplyLocalAvatar()
    {
        if (!Runner) return;

        NetworkObject playerObject = Runner.GetPlayerObject(Runner.LocalPlayer);

        if (!NetworkObjectBehaviourReferences.TryGet(playerObject, 
                out NetworkPlayerCharacter playerCharacter))
            return;

        ApplyLocalSelectionToPlayer(playerCharacter);
    }

    private int FindAvatarForPlayer(PlayerRef player)
    {
        for (int i = 0; i < MaxAvatars; i++)
        {
            if (AvatarOwners[i] == player)
                return i;
        }

        return -1;
    }

    private static bool IsValidAvatarIndex(int avatarIndex)
    {
        return avatarIndex >= 0 && avatarIndex < MaxAvatars;
    }

    private void OnOccupiedAvatarMaskChanged()
    {
        selectionUI?.Refresh(OccupiedAvatarMask, LocalAvatarIndex);
    }

    private static string GetAvatarName(int index)
    {
        string[] names =
        {
            "Football",
            "Basketball",
            "Baseball",
            "Bowling Ball",
            "8-Ball",
            "Golf Ball",
            "Beach Ball",
            "Tennis Ball",
            "Volleyball",
            "Dirt Ball"
        };

        return index >= 0 && index < names.Length ? names[index] : "Unknown";
    }
}