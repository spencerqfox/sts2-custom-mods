using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace FriendTrading.RestSite;

internal sealed partial class FriendRelicTradeSelectionScreen : Control, IOverlayScreen, IScreenContext
{
    private const int Columns = 5;
    private static readonly Vector2 HolderMinimumSize = new(180f, 180f);

    private readonly TaskCompletionSource<RelicModel?> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<NRelicBasicHolder> _holders = new();
    private IReadOnlyList<RelicModel> _relics = Array.Empty<RelicModel>();
    private Godot.Button? _cancelButton;
    private Tween? _fadeTween;
    private bool _completed;

    public NetScreenType ScreenType => NetScreenType.Rewards;

    public bool UseSharedBackstop => true;

    public Control? DefaultFocusedControl => _holders.Count > 0 ? _holders[0] : _cancelButton;

    public static async Task<RelicModel?> Select(Player player, IReadOnlyList<RelicModel> relics)
    {
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
        RelicModel? relic;

        if (ShouldSelectLocalRelic(player))
        {
            FriendRelicTradeSelectionScreen screen = ShowScreen(relics);
            if (LocalContext.IsMe(player))
            {
                foreach (RelicModel candidate in relics)
                {
                    SaveManager.Instance.MarkRelicAsSeen(candidate);
                }
            }

            relic = await screen.RelicSelected();
            int index = relics.IndexOf(relic);
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                player,
                choiceId,
                PlayerChoiceResult.FromIndex(index));
        }
        else
        {
            int index = (await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(player, choiceId)).AsIndex();
            relic = index < 0 ? null : relics[index];
        }

        return relic;
    }

    public override void _Ready()
    {
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Stop;

        BuildTitle();
        BuildRelicGrid();
        BuildCancelButton();
        UpdateFocusNeighbors();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed(MegaInput.cancel))
        {
            Complete(null);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        _fadeTween?.Kill();
        _completionSource.TrySetResult(null);
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
        _fadeTween?.Kill();
        this.QueueFreeSafely();
    }

    public void AfterOverlayShown()
    {
        Visible = true;
    }

    public void AfterOverlayHidden()
    {
        Visible = false;
    }

    private static bool ShouldSelectLocalRelic(Player player)
    {
        if (LocalContext.IsMe(player))
        {
            return RunManager.Instance.NetService.Type != NetGameType.Replay;
        }

        return false;
    }

    private static FriendRelicTradeSelectionScreen ShowScreen(IReadOnlyList<RelicModel> relics)
    {
        FriendRelicTradeSelectionScreen screen = new()
        {
            Name = "FriendRelicTradeSelectionScreen",
            _relics = relics
        };

        NOverlayStack.Instance!.Push(screen);
        return screen;
    }

    private void BuildTitle()
    {
        Label title = new()
        {
            Text = "Choose a relic to trade",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = Colors.White
        };
        title.AddThemeFontSizeOverride("font_size", 42);
        title.AnchorLeft = 0f;
        title.AnchorTop = 0.08f;
        title.AnchorRight = 1f;
        title.AnchorBottom = 0.16f;
        this.AddChildSafely(title);
    }

    private void BuildRelicGrid()
    {
        ScrollContainer scroll = new()
        {
            FollowFocus = true,
            DrawFocusBorder = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            ScrollVerticalCustomStep = 80f
        };
        scroll.AnchorLeft = 0.12f;
        scroll.AnchorTop = 0.2f;
        scroll.AnchorRight = 0.88f;
        scroll.AnchorBottom = 0.78f;
        this.AddChildSafely(scroll);

        GridContainer grid = new()
        {
            Columns = Columns
        };
        grid.AddThemeConstantOverride("h_separation", 42);
        grid.AddThemeConstantOverride("v_separation", 42);
        scroll.AddChildSafely(grid);

        foreach (RelicModel relic in _relics)
        {
            NRelicBasicHolder? holder = NRelicBasicHolder.Create(relic);
            if (holder == null)
            {
                continue;
            }

            holder.CustomMinimumSize = HolderMinimumSize;
            holder.Scale = Vector2.One * 1.5f;
            holder.FocusMode = FocusModeEnum.All;
            _holders.Add(holder);
            grid.AddChildSafely(holder);
            holder.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Complete(holder.Relic.Model)));
        }
    }

    private void BuildCancelButton()
    {
        _cancelButton = new Godot.Button
        {
            Text = "Cancel",
            CustomMinimumSize = new Vector2(240f, 64f),
            FocusMode = FocusModeEnum.All
        };
        _cancelButton.AnchorLeft = 0.5f;
        _cancelButton.AnchorTop = 0.84f;
        _cancelButton.AnchorRight = 0.5f;
        _cancelButton.AnchorBottom = 0.84f;
        _cancelButton.OffsetLeft = -120f;
        _cancelButton.OffsetTop = 0f;
        _cancelButton.OffsetRight = 120f;
        _cancelButton.OffsetBottom = 64f;
        _cancelButton.Pressed += () => Complete(null);
        this.AddChildSafely(_cancelButton);
    }

    private void UpdateFocusNeighbors()
    {
        for (int i = 0; i < _holders.Count; i++)
        {
            NRelicBasicHolder holder = _holders[i];
            int rowStart = i - i % Columns;
            int rowEnd = Math.Min(rowStart + Columns, _holders.Count) - 1;

            holder.FocusNeighborLeft = _holders[Math.Max(rowStart, i - 1)].GetPath();
            holder.FocusNeighborRight = _holders[Math.Min(rowEnd, i + 1)].GetPath();
            holder.FocusNeighborTop = i >= Columns ? _holders[i - Columns].GetPath() : holder.GetPath();
            holder.FocusNeighborBottom = i + Columns < _holders.Count
                ? _holders[i + Columns].GetPath()
                : (_cancelButton?.GetPath() ?? holder.GetPath());
        }

        if (_cancelButton == null)
        {
            return;
        }

        Control topNeighbor = _holders.Count == 0
            ? _cancelButton
            : _holders[Math.Max(0, _holders.Count - Math.Min(Columns, _holders.Count))];
        _cancelButton.FocusNeighborTop = topNeighbor.GetPath();
        _cancelButton.FocusNeighborBottom = _cancelButton.GetPath();
        _cancelButton.FocusNeighborLeft = _cancelButton.GetPath();
        _cancelButton.FocusNeighborRight = _cancelButton.GetPath();
    }

    private async Task<RelicModel?> RelicSelected()
    {
        RelicModel? relic = await _completionSource.Task;
        NOverlayStack.Instance?.Remove(this);
        return relic;
    }

    private void Complete(RelicModel? relic)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _completionSource.TrySetResult(relic);
    }
}
