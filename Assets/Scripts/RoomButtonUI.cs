using System;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomButtonUI : MonoBehaviour
{
    [SerializeField] private TMP_Text roomText;
    [SerializeField] private Button button;

    private SessionInfo sessionInfo;
    private Action<SessionInfo> onClick;

    public void Init(SessionInfo session, Action<SessionInfo> clickAction)
    {
        sessionInfo = session;
        onClick = clickAction;

        roomText.text = $"{session.Name} ({session.PlayerCount}/{session.MaxPlayers})";

        button.interactable = session.IsOpen && session.PlayerCount < session.MaxPlayers;
    }

    private void Awake()
    {
        button.onClick.AddListener(Click);
    }

    private void OnDestroy()
    {
        button.onClick.RemoveListener(Click);
    }

    private void Click()
    {
        onClick?.Invoke(sessionInfo);
    }
}