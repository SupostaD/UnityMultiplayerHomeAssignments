using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameChatUI : MonoBehaviour
{
     public static GameChatUI Instance { get; private set; }

    [Header("Chat Input")]
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private TMP_Dropdown targetDropdown;

    [Header("Chat Scroll View")]
    [SerializeField] private ScrollRect chatScrollRect;
    [SerializeField] private RectTransform chatContent;
    [SerializeField] private ChatMessageRowUI messageRowPrefab;

    private readonly List<ChatMessageRowUI> messageRows = new List<ChatMessageRowUI>();
    private readonly List<PlayerRef> dropdownPlayers = new List<PlayerRef>();
    private bool isChatInputActive;
    private int chatOpenedFrame = -1;
    private int chatClosedFrame = -1;

    private const int MaxChatLines = 20;
    private const int AllOptionIndex = 0;

    private void Awake()
    {
        Instance = this;

        if (chatScrollRect != null && chatContent == null)
            chatContent = chatScrollRect.content;
    }

    private void Start()
    {
        if (chatInputField != null)
        {
            chatInputField.lineType = TMP_InputField.LineType.SingleLine;
            chatInputField.characterLimit = 60;

            chatInputField.onSelect.AddListener(OnChatInputSelected);
            chatInputField.onDeselect.AddListener(OnChatInputDeselected);
            chatInputField.onSubmit.AddListener(OnInputSubmitted);
        }

        InvokeRepeating(nameof(RefreshTargetDropdown), 0.5f, 1f);

        RefreshTargetDropdown();
    }

    private void Update()
    {
        if (chatInputField == null)
            return;

        bool enterPressed =
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter);

        if (!isChatInputActive)
        {
            if (enterPressed &&
                Time.frameCount != chatClosedFrame)
            {
                BeginChatInput();
            }

            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseChatInput(true);
            return;
        }

        if (enterPressed && !chatInputField.isFocused)
            FocusChatInput();
    }

    private void OnDestroy()
    {
        CancelInvoke();

        if (chatInputField != null)
        {
            chatInputField.onSelect.RemoveListener(OnChatInputSelected);
            chatInputField.onDeselect.RemoveListener(OnChatInputDeselected);
            chatInputField.onSubmit.RemoveListener(OnInputSubmitted);
        }

        GameInputBlocker.UnblockGameplayInput();

        if (Instance == this)
            Instance = null;
    }
    
    private void OnChatInputSelected(string text)
    {
        isChatInputActive = true;
        GameInputBlocker.BlockGameplayInput();
        ShowCursor();
    }

    private void OnChatInputDeselected(string text)
    {
        if (!isChatInputActive)
            GameInputBlocker.UnblockGameplayInput();
    }

    private void OnInputSubmitted(string text)
    {
        if (!isChatInputActive ||
            Time.frameCount == chatOpenedFrame)
        {
            return;
        }

        SendCurrentMessage();
    }

    private void BeginChatInput()
    {
        if (chatInputField == null ||
            !chatInputField.interactable ||
            GameInputBlocker.IsGameplayInputBlocked)
        {
            return;
        }

        GameSceneManager sceneManager =
            GameSceneManager.Instance;

        if (sceneManager != null &&
            (sceneManager.IsGameEnded ||
             sceneManager.IsLocalPlayerEliminated))
        {
            return;
        }

        isChatInputActive = true;
        chatOpenedFrame = Time.frameCount;
        GameInputBlocker.BlockGameplayInput();
        ShowCursor();
        FocusChatInput();
    }

    private void FocusChatInput()
    {
        if (chatInputField == null)
            return;

        chatInputField.Select();
        chatInputField.ActivateInputField();
        chatInputField.MoveTextEnd(false);
    }

    private static void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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

        string localName = ChatLocalData.PlayerName;
        int localCharacterIndex = chatNetwork.GetLocalPlayerCharacterIndex();

        if (IsAllSelected())
        {
            AddOwnGlobalMessage(localName, localCharacterIndex, message);
            chatNetwork.SendGlobalMessage(message);
        }
        else
        {
            if (!TryGetSelectedPrivatePlayer(out PlayerRef targetPlayer))
            {
                Debug.LogWarning("GameChatUI: Private target not found.");
                return;
            }

            string targetName = chatNetwork.GetPlayerName(targetPlayer);

            AddOwnPrivateMessage(targetName, localName, localCharacterIndex, message);
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
        CloseChatInput(true);
    }

    private void CloseChatInput(bool clearText)
    {
        isChatInputActive = false;
        chatClosedFrame = Time.frameCount;

        if (chatInputField != null)
        {
            if (clearText)
                chatInputField.text = string.Empty;

            chatInputField.DeactivateInputField();
        }

        GameInputBlocker.UnblockGameplayInput();
    }

    private void RefreshTargetDropdown()
    {
        if (targetDropdown == null)
            return;

        GameChatNetwork chatNetwork = GameChatNetwork.Instance;

        if (chatNetwork == null || !chatNetwork.IsReady())
            return;

        int oldValue = targetDropdown.value;

        dropdownPlayers.Clear();
        targetDropdown.ClearOptions();

        List<string> options = new List<string>();
        options.Add("All");

        PlayerRef localPlayer = chatNetwork.GetLocalPlayer();

        foreach (PlayerRef player in chatNetwork.GetActivePlayers())
        {
            if (player == localPlayer)
                continue;

            dropdownPlayers.Add(player);

            string playerName = chatNetwork.GetPlayerName(player);
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
        if (chatContent == null)
        {
            Debug.LogWarning("GameChatUI: Chat Content is not assigned.");
            return;
        }

        if (messageRowPrefab == null)
        {
            Debug.LogWarning("GameChatUI: Message Row Prefab is not assigned.");
            return;
        }

        ChatMessageRowUI row = Instantiate(messageRowPrefab, chatContent);
        row.Init(line);

        messageRows.Add(row);

        while (messageRows.Count > MaxChatLines)
        {
            ChatMessageRowUI oldRow = messageRows[0];
            messageRows.RemoveAt(0);

            if (oldRow != null)
                Destroy(oldRow.gameObject);
        }

        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        if (chatScrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();

        if (chatContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);

        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    private string EscapeRichText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
    
    public void AddOwnGlobalMessage(string senderName, int characterIndex, string message)
    {
        senderName = EscapeRichText(senderName);
        message = EscapeRichText(message);

        string colorHex = "#FFFFFF";

        if (GameChatNetwork.Instance != null)
            colorHex = GameChatNetwork.Instance.GetCharacterColorHex(characterIndex);

        AddLine($"<color=#AAAAAA>[You]</color> <color={colorHex}><b>{senderName}:</b></color> {message}");
    }

    public void AddOwnPrivateMessage(string targetName, string senderName, int characterIndex, string message)
    {
        targetName = EscapeRichText(targetName);
        senderName = EscapeRichText(senderName);
        message = EscapeRichText(message);

        string colorHex = "#FFFFFF";

        if (GameChatNetwork.Instance != null)
            colorHex = GameChatNetwork.Instance.GetCharacterColorHex(characterIndex);

        AddLine($"<color=#FFAA00><b>Private to {targetName}</b></color> <color={colorHex}><b>{senderName}:</b></color> {message}");
    }
}
