using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

public class GameChatUI : MonoBehaviour
{
    public static GameChatUI Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private TMP_Text chatMessagesText;
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private TMP_Dropdown targetDropdown;

    private NetworkRunner runner;

    private readonly List<string> chatLines = new List<string>();
    private readonly List<PlayerRef> dropdownPlayers = new List<PlayerRef>();

    private const int MaxChatLines = 20;

    // Dropdown index 0 = All
    private const int AllOptionIndex = 0;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        runner = FindObjectOfType<NetworkRunner>();

        if (chatInputField != null)
        {
            chatInputField.lineType = TMP_InputField.LineType.SingleLine;
            chatInputField.characterLimit = 60;
            chatInputField.onSubmit.AddListener(OnInputSubmitted);
            chatInputField.ActivateInputField();
        }

        InvokeRepeating(nameof(RefreshTargetDropdown), 0.5f, 1f);

        RefreshTargetDropdown();
    }

    private void Update()
    {
        // Запасной вариант, если TMP_InputField.onSubmit вдруг тупит.
        if (chatInputField == null)
            return;

        if (!chatInputField.isFocused)
            return;

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SendCurrentMessage();
        }
    }

    private void OnDestroy()
    {
        if (chatInputField != null)
            chatInputField.onSubmit.RemoveListener(OnInputSubmitted);

        if (Instance == this)
            Instance = null;
    }

    private void OnInputSubmitted(string text)
    {
        SendCurrentMessage();
    }

    private void SendCurrentMessage()
    {
        string message = GetInputMessage();

        if (string.IsNullOrWhiteSpace(message))
        {
            ClearInput();
            return;
        }

        GameChatNetwork chatNetwork = GameChatNetwork.Instance;

        if (chatNetwork == null)
        {
            Debug.LogWarning("GameChatUI: GameChatNetwork not found.");
            return;
        }

        if (IsAllSelected())
        {
            chatNetwork.SendGlobalMessage(message);
        }
        else
        {
            if (!TryGetSelectedPrivatePlayer(out PlayerRef targetPlayer))
            {
                Debug.LogWarning("GameChatUI: Private target not found.");
                return;
            }

            chatNetwork.SendPrivateMessage(targetPlayer, message);
        }

        ClearInput();
    }

    private bool IsAllSelected()
    {
        if (targetDropdown == null)
            return true;

        return targetDropdown.value == AllOptionIndex;
    }

    private string GetInputMessage()
    {
        if (chatInputField == null)
            return string.Empty;

        return chatInputField.text.Trim();
    }

    private void ClearInput()
    {
        if (chatInputField == null)
            return;

        chatInputField.text = string.Empty;
        chatInputField.ActivateInputField();
    }

    private void RefreshTargetDropdown()
    {
        if (targetDropdown == null)
            return;

        if (runner == null)
            runner = FindObjectOfType<NetworkRunner>();

        if (runner == null)
            return;

        int oldValue = targetDropdown.value;

        dropdownPlayers.Clear();
        targetDropdown.ClearOptions();

        List<string> options = new List<string>();

        // 0 всегда All
        options.Add("All");

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (player == runner.LocalPlayer)
                continue;

            dropdownPlayers.Add(player);

            string playerName = GetPlayerName(player);
            options.Add(playerName);
        }

        targetDropdown.AddOptions(options);

        if (oldValue >= 0 && oldValue < options.Count)
            targetDropdown.value = oldValue;
        else
            targetDropdown.value = AllOptionIndex;

        targetDropdown.RefreshShownValue();
    }

    private string GetPlayerName(PlayerRef player)
    {
        GameChatNetwork chatNetwork = GameChatNetwork.Instance;

        if (chatNetwork != null)
            return chatNetwork.GetPlayerName(player);

        return "Player " + player.PlayerId;
    }

    private bool TryGetSelectedPrivatePlayer(out PlayerRef player)
    {
        player = default;

        if (targetDropdown == null)
            return false;

        int selectedIndex = targetDropdown.value;

        // 0 = All, значит private игроки начинаются с dropdown index 1
        int playerIndex = selectedIndex - 1;

        if (playerIndex < 0 || playerIndex >= dropdownPlayers.Count)
            return false;

        player = dropdownPlayers[playerIndex];
        return true;
    }

    public void AddGlobalMessage(string senderName, int characterIndex, string message)
    {
        senderName = EscapeRichText(senderName);
        message = EscapeRichText(message);

        string colorHex = "#FFFFFF";

        if (GameChatNetwork.Instance != null)
            colorHex = GameChatNetwork.Instance.GetCharacterColorHex(characterIndex);

        AddLine($"<color={colorHex}><b>{senderName}:</b></color> {message}");
    }

    public void AddPrivateMessage(string senderName, int characterIndex, string message)
    {
        senderName = EscapeRichText(senderName);
        message = EscapeRichText(message);

        string colorHex = "#FFFFFF";

        if (GameChatNetwork.Instance != null)
            colorHex = GameChatNetwork.Instance.GetCharacterColorHex(characterIndex);

        AddLine($"<color=#FFAA00><b>Private from</b></color> <color={colorHex}><b>{senderName}:</b></color> {message}");
    }

    private void AddLine(string line)
    {
        chatLines.Add(line);

        while (chatLines.Count > MaxChatLines)
            chatLines.RemoveAt(0);

        if (chatMessagesText != null)
            chatMessagesText.text = string.Join("\n", chatLines);
    }

    private string EscapeRichText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}