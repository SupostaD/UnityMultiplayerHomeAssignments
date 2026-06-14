using UnityEngine;

public class LobbyPanelsUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject playerNamePanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private GameObject waitingPanel;

    public LobbyPanelState CurrentState { get; private set; }

    private void Awake()
    {
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        Show(LobbyPanelState.MainMenu);
    }

    public void ShowPlayerName()
    {
        Show(LobbyPanelState.PlayerName);
    }

    public void ShowLobby()
    {
        Show(LobbyPanelState.Lobby);
    }

    public void ShowRooms()
    {
        Show(LobbyPanelState.Rooms);
    }

    public void ShowWaiting()
    {
        Show(LobbyPanelState.Waiting);
    }

    public void Show(LobbyPanelState state)
    {
        CurrentState = state;

        menuPanel.SetActive(state == LobbyPanelState.MainMenu);
        playerNamePanel.SetActive(state == LobbyPanelState.PlayerName);
        lobbyPanel.SetActive(state == LobbyPanelState.Lobby);
        roomPanel.SetActive(state == LobbyPanelState.Rooms);
        waitingPanel.SetActive(state == LobbyPanelState.Waiting);
    }
}
