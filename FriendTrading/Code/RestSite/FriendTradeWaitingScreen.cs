using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace FriendTrading.RestSite;

internal sealed partial class FriendTradeWaitingScreen : Control, IOverlayScreen, IScreenContext
{
    private Func<bool> _onCancel = null!;
    private Player _target = null!;
    private AbstractModel _tradeItem = null!;
    private NChoiceSelectionSkipButton? _cancelButton;
    private Tween? _fadeTween;
    private bool _cancelRequested;
    private bool _closed;

    public NetScreenType ScreenType => NetScreenType.CardSelection;

    public bool UseSharedBackstop => true;

    public Control? DefaultFocusedControl => _cancelButton;

    public override void _Ready()
    {
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Stop;

        BuildBanner();
        BuildDescription();
        BuildTradeItemPanel();
        BuildCancelButton();
    }

    public override void _ExitTree()
    {
        _fadeTween?.Kill();
    }

    public void AfterOverlayOpened()
    {
        Modulate = Colors.Transparent;
        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(this, "modulate:a", 1f, 0.2);
    }

    public void AfterOverlayClosed()
    {
        _closed = true;
        _fadeTween?.Kill();
        this.QueueFreeSafely();
    }

    public void AfterOverlayShown()
    {
        Visible = true;
        if (!_cancelRequested)
        {
            _cancelButton?.Enable();
        }
    }

    public void AfterOverlayHidden()
    {
        _cancelButton?.Disable();
        Visible = false;
    }

    public static FriendTradeWaitingScreen? Show(
        AbstractModel tradeItem,
        Player target,
        Func<bool> onCancel)
    {
        NOverlayStack? overlayStack = NOverlayStack.Instance;
        if (overlayStack == null)
        {
            return null;
        }

        FriendTradeWaitingScreen screen = new()
        {
            Name = "FriendTradeWaitingScreen",
            _tradeItem = tradeItem,
            _target = target,
            _onCancel = onCancel
        };
        overlayStack.Push(screen);
        return screen;
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        NOverlayStack? overlayStack = NOverlayStack.Instance;
        if (overlayStack == null)
        {
            this.QueueFreeSafely();
            return;
        }

        overlayStack.Remove(this);
    }

    private void BuildBanner()
    {
        NCommonBanner banner = SceneHelper.Instantiate<NCommonBanner>("ui/common_banner");
        this.AddChildSafely(banner);
        banner.label.SetTextAutoSize(GetText("FRIEND_TRADE_WAITING_TITLE"));
        banner.AnimateIn();
    }

    private void BuildDescription()
    {
        Label description = new()
        {
            Text = GetDescriptionText(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(0.88f, 0.88f, 0.88f)
        };
        description.AddThemeFontSizeOverride("font_size", 24);
        description.AnchorLeft = 0.1f;
        description.AnchorTop = 0.16f;
        description.AnchorRight = 0.9f;
        description.AnchorBottom = 0.22f;
        this.AddChildSafely(description);
    }

    private void BuildTradeItemPanel()
    {
        PanelContainer panel = new();
        panel.AnchorLeft = 0.5f;
        panel.AnchorTop = 0.23f;
        panel.AnchorRight = 0.5f;
        panel.AnchorBottom = 0.75f;
        panel.OffsetLeft = -230f;
        panel.OffsetRight = 230f;

        StyleBoxFlat panelStyle = new()
        {
            BgColor = new Color(0.08f, 0.09f, 0.12f, 0.94f),
            BorderColor = new Color(1f, 1f, 1f, 0.16f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 16,
            CornerRadiusTopRight = 16,
            CornerRadiusBottomRight = 16,
            CornerRadiusBottomLeft = 16
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        this.AddChildSafely(panel);

        CenterContainer center = new();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.AddChildSafely(center);

        switch (_tradeItem)
        {
            case CardModel card:
                AddCard(center, card);
                break;
            case RelicModel relic:
                AddRelic(center, relic);
                break;
        }
    }

    private static void AddCard(Node parent, CardModel card)
    {
        NCard? cardNode = NCard.Create(card);
        if (cardNode == null)
        {
            return;
        }

        NPreviewCardHolder? holder = NPreviewCardHolder.Create(
            cardNode,
            showHoverTips: true,
            scaleOnHover: true);
        if (holder == null)
        {
            cardNode.QueueFreeSafely();
            return;
        }

        holder.SetCardScale(Vector2.One * 0.8f);
        holder.FocusMode = FocusModeEnum.None;
        parent.AddChildSafely(holder);
        cardNode.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
    }

    private static void AddRelic(Node parent, RelicModel relic)
    {
        NRelicBasicHolder? holder = NRelicBasicHolder.Create(relic);
        if (holder == null)
        {
            return;
        }

        holder.Scale = Vector2.One * 1.5f;
        holder.FocusMode = FocusModeEnum.None;
        parent.AddChildSafely(holder);
    }

    private void BuildCancelButton()
    {
        _cancelButton = SceneHelper.Instantiate<NChoiceSelectionSkipButton>(
            "ui/choice_selection_skip_button");
        _cancelButton.Set(
            NChoiceSelectionSkipButton.PropertyName._optionName,
            GetText("FRIEND_TRADE_WAITING_CANCEL"));
        this.AddChildSafely(_cancelButton);
        NodePath cancelPath = _cancelButton.GetPath();
        _cancelButton.FocusNeighborLeft = cancelPath;
        _cancelButton.FocusNeighborTop = cancelPath;
        _cancelButton.FocusNeighborRight = cancelPath;
        _cancelButton.FocusNeighborBottom = cancelPath;
        _cancelButton.Connect(
            NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => RequestCancel()));
        _cancelButton.AnimateIn();
    }

    private void RequestCancel()
    {
        if (_cancelRequested || _closed)
        {
            return;
        }

        _cancelRequested = true;
        _cancelButton?.Disable();
        if (_onCancel())
        {
            Close();
        }
    }

    private static string GetText(string key)
    {
        return new LocString("rest_site_ui", key).GetFormattedText();
    }

    private string GetDescriptionText()
    {
        string targetName = PlatformUtil.GetPlayerNameRaw(
            RunManager.Instance.NetService.Platform,
            _target.NetId);
        if (string.IsNullOrWhiteSpace(targetName))
        {
            targetName = _target.Character.Title.GetFormattedText();
        }

        LocString description = new("rest_site_ui", "FRIEND_TRADE_WAITING_DESCRIPTION");
        description.Add("Target", targetName);
        return description.GetFormattedText();
    }
}
