using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace FriendTrading.RestSite;

internal static class FriendTradeCoordinator
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<(FriendTradeKind Kind, ulong SenderId), PendingTrade> PendingOffers = new();
    private static readonly HashSet<(FriendTradeKind Kind, ulong PlayerId)> UnavailablePlayers = new();
    private static PlayerChoiceSynchronizer? SubscribedChoiceSynchronizer;

    public static void Reset()
    {
        List<PendingTrade> pendingTrades;

        lock (SyncRoot)
        {
            pendingTrades = PendingOffers.Values.ToList();
            PendingOffers.Clear();
            UnavailablePlayers.Clear();
        }

        foreach (PendingTrade pending in pendingTrades)
        {
            ResolvePending(pending, success: false);
        }
    }

    public static void SyncAvailabilityFromOptions(Player player, IReadOnlyList<RestSiteOption> options)
    {
        RejectPendingOffers(MarkUnavailableKindsMissingFromOptions(player.NetId, options));
    }

    public static void SubscribeToChoiceResults()
    {
        PlayerChoiceSynchronizer synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
        if (ReferenceEquals(SubscribedChoiceSynchronizer, synchronizer))
        {
            return;
        }

        if (SubscribedChoiceSynchronizer != null)
        {
            SubscribedChoiceSynchronizer.PlayerChoiceReceived -= OnPlayerChoiceReceived;
        }

        SubscribedChoiceSynchronizer = synchronizer;
        synchronizer.PlayerChoiceReceived += OnPlayerChoiceReceived;
    }

    public static async Task<bool> SelectSubmissionIntent(FriendTradeKind kind, Player sender, Player target)
    {
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(sender);

        if (LocalContext.IsMe(sender))
        {
            bool isReciprocal = HasReciprocalOffer(kind, sender, target);
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                sender,
                choiceId,
                PlayerChoiceResult.FromIndex(isReciprocal ? 1 : 0));
            return isReciprocal;
        }

        int result = (await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(sender, choiceId))
            .AsIndex();
        return result == 1;
    }

    public static Task<bool> Submit(FriendTradeOffer offer)
    {
        if (!offer.Payload.CanTradeTo(offer.Target))
        {
            return Task.FromResult(false);
        }

        PendingTrade? counterpart = null;
        PendingTrade? pending = null;
        bool shouldWait = false;

        lock (SyncRoot)
        {
            var counterpartKey = (offer.Kind, offer.Target.NetId);
            if (PendingOffers.TryGetValue(counterpartKey, out PendingTrade? waitingOffer) &&
                waitingOffer.Offer.Target.NetId == offer.Sender.NetId &&
                offer.IsReciprocal)
            {
                waitingOffer.ConfirmationClaimed = true;
                counterpart = waitingOffer;
            }
            else
            {
                if (offer.IsReciprocal)
                {
                    return Task.FromResult(false);
                }

                if (IsUnavailable(offer.Sender.NetId, offer.Kind) ||
                    IsUnavailable(offer.Target.NetId, offer.Kind))
                {
                    return Task.FromResult(false);
                }

                var offerKey = (offer.Kind, offer.Sender.NetId);
                pending = new PendingTrade(offer);
                PendingOffers[offerKey] = pending;
                shouldWait = true;
            }
        }

        if (shouldWait)
        {
            if (!ShowWaitingScreen(offer))
            {
                PendingTrade? canceled = CancelPendingOffer(offer);
                if (canceled != null)
                {
                    RejectPendingOffers(new[] { canceled });
                }
            }

            return pending!.Completion.Task;
        }

        return CompletePairAndResolve(offer, counterpart!);
    }

    private static bool HasReciprocalOffer(FriendTradeKind kind, Player sender, Player target)
    {
        lock (SyncRoot)
        {
            return PendingOffers.TryGetValue((kind, target.NetId), out PendingTrade? waitingOffer) &&
                   waitingOffer.Offer.Target.NetId == sender.NetId;
        }
    }

    public static void OnRestSiteOptionStarted(RestSiteOption option, ulong playerId)
    {
        List<PendingTrade> canceled;

        lock (SyncRoot)
        {
            canceled = option is FriendTradeRestSiteOption
                ? CancelPendingOffersFromSenderLocked(playerId)
                : CancelPendingOffersInvolvingLocked(playerId);
        }

        RejectPendingOffers(canceled);
    }

    public static void OnRestSiteOptionFinished(RestSiteOption option, bool success, ulong playerId)
    {
        if (!success)
        {
            return;
        }

        RejectPendingOffers(CancelPendingOffersInvolving(playerId));
        TaskHelper.RunSafely(SyncAvailabilityAfterOptionsUpdate(playerId));
    }

    public static void CancelLocalPendingOffers()
    {
        if (!LocalContext.NetId.HasValue)
        {
            return;
        }

        RejectPendingOffers(CancelPendingOffersInvolving(LocalContext.NetId.Value));
    }

    public static void CancelAllPendingOffers()
    {
        List<PendingTrade> canceled;

        lock (SyncRoot)
        {
            canceled = PendingOffers.Values
                .Where(pending => !pending.ConfirmationClaimed && !pending.ConfirmationSent)
                .ToList();

            foreach (PendingTrade pending in canceled)
            {
                PendingOffers.Remove((pending.Offer.Kind, pending.Offer.Sender.NetId));
            }
        }

        RejectPendingOffers(canceled);
    }

    private static async Task SyncAvailabilityAfterOptionsUpdate(ulong playerId)
    {
        await Task.Yield();

        IReadOnlyList<RestSiteOption> options = RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(playerId);
        RejectPendingOffers(MarkUnavailableKindsMissingFromOptions(playerId, options));
    }

    private static List<PendingTrade> MarkUnavailableKindsMissingFromOptions(
        ulong playerId,
        IReadOnlyList<RestSiteOption> options)
    {
        bool cardTradeAvailable = options.Any(option => option.OptionId == FriendCardTradeRestSiteOption.Id);
        bool relicTradeAvailable = options.Any(option => option.OptionId == FriendRelicTradeRestSiteOption.Id);
        List<PendingTrade> canceled = new();

        lock (SyncRoot)
        {
            if (!cardTradeAvailable)
            {
                UnavailablePlayers.Add((FriendTradeKind.Card, playerId));
            }

            if (!relicTradeAvailable)
            {
                UnavailablePlayers.Add((FriendTradeKind.Relic, playerId));
            }

            foreach (var entry in PendingOffers)
            {
                PendingTrade pending = entry.Value;
                FriendTradeOffer offer = pending.Offer;
                if (!pending.ConfirmationSent &&
                    (IsUnavailable(offer.Sender.NetId, offer.Kind) ||
                     IsUnavailable(offer.Target.NetId, offer.Kind)))
                {
                    canceled.Add(pending);
                }
            }

            foreach (PendingTrade pending in canceled)
            {
                PendingOffers.Remove((pending.Offer.Kind, pending.Offer.Sender.NetId));
            }
        }

        return canceled;
    }

    private static async Task<bool> CompletePairAndResolve(FriendTradeOffer offer, PendingTrade counterpart)
    {
        FriendTradeOffer first = offer;
        FriendTradeOffer second = counterpart.Offer;
        if (second.Sender.NetId < first.Sender.NetId)
        {
            first = counterpart.Offer;
            second = offer;
        }

        // Only the pending offer's owner decides whether the pair is still valid so
        // every peer commits or rejects the trade from the same synchronized choice.
        if (!await ConfirmPendingOffer(counterpart.Offer, first, second))
        {
            ResolvePending(CancelPendingOffer(counterpart.Offer) ?? counterpart, success: false);
            return false;
        }

        DisableLocalOptionsIfPlayer(first.Sender);
        DisableLocalOptionsIfPlayer(second.Sender);

        if (!await CompletePair(first, second))
        {
            ResolvePending(CancelPendingOffer(counterpart.Offer) ?? counterpart, success: false);
            EnableLocalOptionsIfPlayer(counterpart.Offer.Sender);
            return false;
        }

        ResolvePending(CancelPendingOffer(counterpart.Offer) ?? counterpart, success: true);
        return true;
    }

    private static async Task<bool> ConfirmPendingOffer(
        FriendTradeOffer pendingOffer,
        FriendTradeOffer first,
        FriendTradeOffer second)
    {
        if (LocalContext.IsMe(pendingOffer.Sender))
        {
            bool canAccept = CanCompletePair(first, second) && CanConsumePendingOffer(pendingOffer);
            bool accepted;
            bool shouldSend;

            lock (SyncRoot)
            {
                shouldSend = PendingOffers.TryGetValue(
                    (pendingOffer.Kind, pendingOffer.Sender.NetId),
                    out PendingTrade? pending) &&
                    pending.Offer == pendingOffer;

                accepted = shouldSend &&
                           canAccept &&
                           !IsUnavailable(pendingOffer.Sender.NetId, pendingOffer.Kind) &&
                           !IsUnavailable(pendingOffer.Target.NetId, pendingOffer.Kind);

                if (shouldSend)
                {
                    pending!.ConfirmationSent = true;
                }
            }

            if (shouldSend)
            {
                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                    pendingOffer.Sender,
                    pendingOffer.ConfirmationChoiceId,
                    PlayerChoiceResult.FromIndex(accepted ? 1 : 0));
            }

            CloseWaitingScreen(pendingOffer);

            if (accepted)
            {
                DisableLocalOptionsIfPlayer(pendingOffer.Sender);
            }

            return accepted;
        }

        int result = (await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(
                pendingOffer.Sender,
                pendingOffer.ConfirmationChoiceId))
            .AsIndex();
        return result == 1;
    }

    private static bool CanCompletePair(FriendTradeOffer offer, FriendTradeOffer counterpart)
    {
        return offer.Payload.CanTradeTo(offer.Target) &&
               counterpart.Payload.CanTradeTo(counterpart.Target);
    }

    private static bool CanConsumePendingOffer(FriendTradeOffer offer)
    {
        lock (SyncRoot)
        {
            if (!PendingOffers.TryGetValue((offer.Kind, offer.Sender.NetId), out PendingTrade? pending) ||
                pending.Offer != offer ||
                IsUnavailable(offer.Sender.NetId, offer.Kind) ||
                IsUnavailable(offer.Target.NetId, offer.Kind))
            {
                return false;
            }
        }

        IReadOnlyList<RestSiteOption> options =
            RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(offer.Sender.NetId);
        return options.Contains(offer.Option);
    }

    private static async Task<bool> CompletePair(FriendTradeOffer offer, FriendTradeOffer counterpart)
    {
        try
        {
            if (!offer.Payload.CanTradeTo(offer.Target) ||
                !counterpart.Payload.CanTradeTo(counterpart.Target))
            {
                return false;
            }

            await offer.Payload.RemoveFromSource();
            await counterpart.Payload.RemoveFromSource();

            await offer.Payload.AddToTarget(offer.Target);
            await counterpart.Payload.AddToTarget(counterpart.Target);

            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[FriendTrading] Trade failed: {ex}");
            await RollBack(offer.Payload, counterpart.Payload);
            return false;
        }
    }

    private static async Task RollBack(IFriendTradePayload first, IFriendTradePayload second)
    {
        await RollBackTarget(second);
        await RollBackTarget(first);
        await RollBackSource(second);
        await RollBackSource(first);
    }

    private static async Task RollBackTarget(IFriendTradePayload payload)
    {
        try
        {
            await payload.RemoveFromTarget();
        }
        catch (Exception ex)
        {
            Log.Error($"[FriendTrading] Failed to roll back target item: {ex}");
        }
    }

    private static async Task RollBackSource(IFriendTradePayload payload)
    {
        try
        {
            await payload.RestoreToSource();
        }
        catch (Exception ex)
        {
            Log.Error($"[FriendTrading] Failed to restore source item: {ex}");
        }
    }

    private static bool IsUnavailable(ulong playerId, FriendTradeKind kind)
    {
        return UnavailablePlayers.Contains((kind, playerId));
    }

    private static List<PendingTrade> CancelPendingOffersInvolving(ulong playerId)
    {
        lock (SyncRoot)
        {
            return CancelPendingOffersInvolvingLocked(playerId);
        }
    }

    private static List<PendingTrade> CancelPendingOffersInvolvingLocked(ulong playerId)
    {
        List<PendingTrade> canceled = PendingOffers
            .Where(entry =>
                !entry.Value.ConfirmationSent &&
                (entry.Value.Offer.Sender.NetId == playerId ||
                 entry.Value.Offer.Target.NetId == playerId))
            .Select(entry => entry.Value)
            .ToList();

        foreach (PendingTrade pending in canceled)
        {
            PendingOffers.Remove((pending.Offer.Kind, pending.Offer.Sender.NetId));
        }

        return canceled;
    }

    private static List<PendingTrade> CancelPendingOffersFromSenderLocked(ulong playerId)
    {
        List<PendingTrade> canceled = PendingOffers
            .Where(entry =>
                !entry.Value.ConfirmationSent &&
                entry.Value.Offer.Sender.NetId == playerId)
            .Select(entry => entry.Value)
            .ToList();

        foreach (PendingTrade pending in canceled)
        {
            PendingOffers.Remove((pending.Offer.Kind, pending.Offer.Sender.NetId));
        }

        return canceled;
    }

    private static PendingTrade? CancelPendingOffer(FriendTradeOffer offer)
    {
        lock (SyncRoot)
        {
            if (PendingOffers.TryGetValue((offer.Kind, offer.Sender.NetId), out PendingTrade? pending) &&
                pending.Offer == offer)
            {
                PendingOffers.Remove((offer.Kind, offer.Sender.NetId));
                return pending;
            }
        }

        return null;
    }

    private static void RejectPendingOffers(IEnumerable<PendingTrade> pendingTrades)
    {
        foreach (PendingTrade pending in pendingTrades)
        {
            FriendTradeOffer offer = pending.Offer;

            ResolvePending(pending, success: false);

            if (!pending.ConfirmationSent && LocalContext.IsMe(offer.Sender))
            {
                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                    offer.Sender,
                    offer.ConfirmationChoiceId,
                    PlayerChoiceResult.FromIndex(0));
            }
        }
    }

    private static void ResolvePending(PendingTrade? pending, bool success)
    {
        if (pending == null)
        {
            return;
        }

        pending.Completion.TrySetResult(success);
        CloseWaitingScreen(pending.Offer);
    }

    private static bool ShowWaitingScreen(FriendTradeOffer offer)
    {
        if (!LocalContext.IsMe(offer.Sender) ||
            RunManager.Instance.NetService.Type == NetGameType.Replay)
        {
            return true;
        }

        FriendTradeWaitingScreen? screen = FriendTradeWaitingScreen.Show(
            offer.Payload.TradeItem,
            offer.Target,
            () => TryCancelFromWaitingScreen(offer));
        if (screen == null)
        {
            lock (SyncRoot)
            {
                return !PendingOffers.TryGetValue(
                        (offer.Kind, offer.Sender.NetId),
                        out PendingTrade? pending) ||
                    pending.Offer != offer ||
                    pending.ConfirmationClaimed ||
                    pending.ConfirmationSent;
            }
        }

        bool stillPending;
        lock (SyncRoot)
        {
            stillPending = PendingOffers.TryGetValue(
                    (offer.Kind, offer.Sender.NetId),
                    out PendingTrade? pending) &&
                pending.Offer == offer &&
                !pending.ConfirmationClaimed &&
                !pending.ConfirmationSent;

            if (stillPending)
            {
                offer.WaitingScreen = screen;
            }
        }

        if (!stillPending)
        {
            CloseWaitingScreen(screen);
        }

        return true;
    }

    private static bool TryCancelFromWaitingScreen(FriendTradeOffer offer)
    {
        PendingTrade canceled;

        lock (SyncRoot)
        {
            if (!PendingOffers.TryGetValue(
                    (offer.Kind, offer.Sender.NetId),
                    out PendingTrade? pending) ||
                pending.Offer != offer ||
                pending.ConfirmationClaimed ||
                pending.ConfirmationSent)
            {
                return false;
            }

            PendingOffers.Remove((offer.Kind, offer.Sender.NetId));
            canceled = pending;
        }

        RejectPendingOffers(new[] { canceled });
        return true;
    }

    private static void CloseWaitingScreen(FriendTradeOffer offer)
    {
        FriendTradeWaitingScreen? screen = offer.WaitingScreen;
        offer.WaitingScreen = null;
        CloseWaitingScreen(screen);
    }

    private static void CloseWaitingScreen(FriendTradeWaitingScreen? screen)
    {
        if (screen == null)
        {
            return;
        }

        try
        {
            screen.Close();
        }
        catch (Exception ex)
        {
            Log.Error($"[FriendTrading] Failed to close the waiting screen: {ex}");
        }
    }

    private static void OnPlayerChoiceReceived(
        Player sender,
        uint choiceId,
        NetPlayerChoiceResult result)
    {
        if (result.indexes == null ||
            result.indexes.Count != 1 ||
            result.indexes[0] != 0)
        {
            return;
        }

        PendingTrade? rejectedPending = null;
        bool shouldConsumeChoice = false;
        lock (SyncRoot)
        {
            (FriendTradeKind Kind, ulong SenderId)? rejectedOfferKey = null;
            foreach (var entry in PendingOffers)
            {
                FriendTradeOffer offer = entry.Value.Offer;
                if (!entry.Value.ConfirmationSent &&
                    offer.Sender.NetId == sender.NetId &&
                    offer.ConfirmationChoiceId == choiceId)
                {
                    rejectedOfferKey = entry.Key;
                    rejectedPending = entry.Value;
                    shouldConsumeChoice = !entry.Value.ConfirmationClaimed;
                    break;
                }
            }

            if (rejectedOfferKey.HasValue)
            {
                PendingOffers.Remove(rejectedOfferKey.Value);
            }
        }

        ResolvePending(rejectedPending, success: false);

        PlayerChoiceSynchronizer? synchronizer = SubscribedChoiceSynchronizer;
        if (shouldConsumeChoice &&
            synchronizer != null &&
            !LocalContext.IsMe(sender) &&
            RunManager.Instance.NetService.Type != NetGameType.Singleplayer)
        {
            TaskHelper.RunSafely(ConsumeRejectedChoice(synchronizer, sender, choiceId));
        }
    }

    private static async Task ConsumeRejectedChoice(
        PlayerChoiceSynchronizer synchronizer,
        Player sender,
        uint choiceId)
    {
        // PlayerChoiceReceived fires just before the base synchronizer buffers an
        // unclaimed result, so defer once and then remove that completed entry.
        await Task.Yield();
        await synchronizer.WaitForRemoteChoice(sender, choiceId);
    }

    private static void DisableLocalOptionsIfPlayer(Player player)
    {
        if (LocalContext.IsMe(player))
        {
            NRestSiteRoom.Instance?.DisableOptions();
        }
    }

    private static void EnableLocalOptionsIfPlayer(Player player)
    {
        if (LocalContext.IsMe(player))
        {
            NRestSiteRoom.Instance?.EnableOptions();
        }
    }

    private sealed class PendingTrade
    {
        public PendingTrade(FriendTradeOffer offer)
        {
            Offer = offer;
        }

        public FriendTradeOffer Offer { get; }

        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ConfirmationClaimed { get; set; }

        public bool ConfirmationSent { get; set; }
    }
}
