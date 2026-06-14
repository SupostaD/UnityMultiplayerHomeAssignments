using System;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomButtonUI : MonoBehaviour
{
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text playersCountText;
    [SerializeField] private Button joinButton;

    private SessionInfo sessionInfo;
    private Action<SessionInfo> onJoinClicked;

    public void Init(SessionInfo session, Action<SessionInfo> onClick)
    {
        sessionInfo = session;
        onJoinClicked = onClick;

        roomNameText.text = session.Name;
        playersCountText.text = $"{session.PlayerCount}/{session.MaxPlayers}";

        bool canJoin = session.IsOpen && session.PlayerCount < session.MaxPlayers;

        joinButton.interactable = canJoin;

        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(Join);
    }

    private void Join()
    {
        if (sessionInfo == null)
            return;

        if (!sessionInfo.IsOpen || sessionInfo.PlayerCount >= sessionInfo.MaxPlayers)
            return;

        onJoinClicked?.Invoke(sessionInfo);
    }
}