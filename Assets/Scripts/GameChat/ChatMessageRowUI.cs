using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatMessageRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private LayoutElement layoutElement;

    [Header("Size")]
    [SerializeField] private float minHeight = 30f;
    [SerializeField] private float verticalPadding = 8f;

    public void Init(string message)
    {
        if (messageText == null)
            return;

        messageText.text = message;
        messageText.enableWordWrapping = true;
        messageText.overflowMode = TextOverflowModes.Overflow;

        if (layoutElement == null)
        {
            Debug.LogError(
                "ChatMessageRowUI: Layout Element is not assigned.",
                this
            );
            return;
        }

        RecalculateHeight();

        StartCoroutine(RecalculateHeightNextFrame());
    }

    private IEnumerator RecalculateHeightNextFrame()
    {
        yield return null;
        RecalculateHeight();
    }

    private void RecalculateHeight()
    {
        if (messageText == null || layoutElement == null)
            return;

        RectTransform textRect = messageText.rectTransform;
        RectTransform rowRect = transform as RectTransform;
        RectTransform parentRect = transform.parent as RectTransform;

        float width = textRect.rect.width;

        if (width <= 1f && rowRect != null)
            width = rowRect.rect.width;

        if (width <= 1f && parentRect != null)
            width = parentRect.rect.width;

        if (width <= 1f)
            width = 300f;

        messageText.ForceMeshUpdate();

        float preferredHeight = messageText.GetPreferredValues(
            messageText.text,
            width,
            Mathf.Infinity
        ).y;

        float finalHeight = Mathf.Max(minHeight, preferredHeight + verticalPadding);

        layoutElement.minHeight = finalHeight;
        layoutElement.preferredHeight = finalHeight;
        layoutElement.flexibleHeight = 0f;
    }
}
