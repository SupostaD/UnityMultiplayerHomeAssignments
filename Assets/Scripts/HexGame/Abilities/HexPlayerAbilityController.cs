using Fusion;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HexPlayerAbilityController : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private HexAbilitySettings settings;

    [Header("Ability Icons")]
    [SerializeField] private Image dashAbilityImage;
    [SerializeField] private Image doubleJumpAbilityImage;

    [Networked]
    public NetworkBool HasDashCharge { get; private set; }

    [Networked]
    public NetworkBool HasDoubleJumpCharge { get; private set; }

    private Color dashVisibleColor;
    private Color doubleJumpVisibleColor;
    private Vector3 dashVisibleScale;
    private Vector3 doubleJumpVisibleScale;
    private bool visualsInitialized;

    private void Awake()
    {
        ValidateReferences();
        InitializeVisuals();
        ApplyVisualsImmediately(false, false);
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            HasDashCharge = false;
            HasDoubleJumpCharge = false;
        }

        InitializeVisuals();
        ApplyVisualsImmediately(
            HasDashCharge,
            HasDoubleJumpCharge
        );
    }

    public override void Render()
    {
        AnimateIcon(
            dashAbilityImage,
            dashVisibleColor,
            dashVisibleScale,
            HasDashCharge
        );
        AnimateIcon(
            doubleJumpAbilityImage,
            doubleJumpVisibleColor,
            doubleJumpVisibleScale,
            HasDoubleJumpCharge
        );
    }

    public bool HasAbility(HexPlayerAbilityType abilityType)
    {
        switch (abilityType)
        {
            case HexPlayerAbilityType.Dash:
                return HasDashCharge;

            case HexPlayerAbilityType.DoubleJump:
                return HasDoubleJumpCharge;

            default:
                return false;
        }
    }

    public bool TryGrantAbility(HexPlayerAbilityType abilityType)
    {
        if (Object == null || !Object.HasStateAuthority)
            return false;

        switch (abilityType)
        {
            case HexPlayerAbilityType.Dash:
                if (HasDashCharge)
                    return false;

                HasDashCharge = true;
                return true;

            case HexPlayerAbilityType.DoubleJump:
                if (HasDoubleJumpCharge)
                    return false;

                HasDoubleJumpCharge = true;
                return true;

            default:
                return false;
        }
    }

    public bool TryConsumeAbility(HexPlayerAbilityType abilityType)
    {
        if (Object == null || !Object.HasStateAuthority)
            return false;

        switch (abilityType)
        {
            case HexPlayerAbilityType.Dash:
                if (!HasDashCharge)
                    return false;

                HasDashCharge = false;
                return true;

            case HexPlayerAbilityType.DoubleJump:
                if (!HasDoubleJumpCharge)
                    return false;

                HasDoubleJumpCharge = false;
                return true;

            default:
                return false;
        }
    }

    private void ValidateReferences()
    {
        if (settings == null)
        {
            Debug.LogError(
                "HexPlayerAbilityController: Ability Settings is not assigned.",
                this
            );
        }

        if (dashAbilityImage == null)
        {
            Debug.LogError(
                "HexPlayerAbilityController: Dash Ability Image is not assigned.",
                this
            );
        }

        if (doubleJumpAbilityImage == null)
        {
            Debug.LogError(
                "HexPlayerAbilityController: Double Jump Ability Image is not assigned.",
                this
            );
        }
    }

    private void InitializeVisuals()
    {
        if (visualsInitialized)
            return;

        CacheIcon(
            dashAbilityImage,
            out dashVisibleColor,
            out dashVisibleScale
        );
        CacheIcon(
            doubleJumpAbilityImage,
            out doubleJumpVisibleColor,
            out doubleJumpVisibleScale
        );

        visualsInitialized = true;
    }

    private static void CacheIcon(
        Image image,
        out Color visibleColor,
        out Vector3 visibleScale)
    {
        if (image == null)
        {
            visibleColor = Color.white;
            visibleScale = Vector3.one;
            return;
        }

        visibleColor = image.color;
        visibleScale = image.rectTransform.localScale;
        image.raycastTarget = false;
    }

    private void ApplyVisualsImmediately(
        bool showDash,
        bool showDoubleJump)
    {
        ApplyIconImmediately(
            dashAbilityImage,
            dashVisibleColor,
            dashVisibleScale,
            showDash
        );
        ApplyIconImmediately(
            doubleJumpAbilityImage,
            doubleJumpVisibleColor,
            doubleJumpVisibleScale,
            showDoubleJump
        );
    }

    private void AnimateIcon(
        Image image,
        Color visibleColor,
        Vector3 visibleScale,
        bool isVisible)
    {
        if (image == null)
            return;

        float safeTransitionSeconds =
            settings != null
                ? settings.IconTransitionSeconds
                : 0.2f;
        float maximumDelta =
            Time.deltaTime / safeTransitionSeconds;
        float targetAlpha =
            isVisible ? visibleColor.a : 0f;
        Color currentColor = image.color;

        currentColor.a = Mathf.MoveTowards(
            currentColor.a,
            targetAlpha,
            maximumDelta
        );
        image.color = currentColor;

        Vector3 hiddenIconScale =
            visibleScale *
            (settings != null
                ? settings.HiddenIconScale
                : 0.75f);
        Vector3 targetScale =
            isVisible ? visibleScale : hiddenIconScale;

        image.rectTransform.localScale = Vector3.MoveTowards(
            image.rectTransform.localScale,
            targetScale,
            maximumDelta *
            Mathf.Max(1f, visibleScale.magnitude)
        );
    }

    private void ApplyIconImmediately(
        Image image,
        Color visibleColor,
        Vector3 visibleScale,
        bool isVisible)
    {
        if (image == null)
            return;

        Color color = visibleColor;
        color.a = isVisible ? visibleColor.a : 0f;
        image.color = color;
        image.rectTransform.localScale = isVisible
            ? visibleScale
            : visibleScale *
              (settings != null
                  ? settings.HiddenIconScale
                  : 0.75f);
    }
}
