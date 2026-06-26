using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace FriendTrading.RestSite;

internal static class FriendTradeCoordinator
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<(FriendTradeKind Kind, ulong SenderId), PendingTrade> PendingOffers = new();
    private static readonly HashSet<(FriendTradeKind Kind, ulong PlayerId)> UnavailablePlayers = new();
    private static readonly HashSet<ulong> ActiveSelections = new();

    public static void Reset()
    {
        lock (SyncRoot)
        {
            PendingOffers.Clear();
            UnavailablePlayers.Clear();
            ActiveSelections.Clear();
        }
    }

    public static void SyncAvailabilityFromOptions(Player player, IReadOnlyList<RestSiteOption> options)
    {
        RejectLocalPendingOffers(MarkUnavailableKindsMissingFromOptions(player.NetId, options));
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

        lock (SyncRoot)
        {
            var counterpartKey = (offer.Kind, offer.Target.NetId);
            if (PendingOffers.TryGetValue(counterpartKey, out PendingTrade? waitingOffer) &&
                waitingOffer.Offer.Target.NetId == offer.Sender.NetId &&
                offer.IsReciprocal)
            {
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
                PendingOffers[offerKey] = new PendingTrade(offer);
                return Task.FromResult(false);
            }
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
        List<FriendTradeOffer> canceled;

        lock (SyncRoot)
        {
            ActiveSelections.Add(playerId);

            canceled = option is FriendTradeRestSiteOption
                ? CancelPendingOffersFromSenderLocked(playerId)
                : CancelPendingOffersInvolvingLocked(playerId);
        }

        RejectLocalPendingOffers(canceled);
    }

    public static void OnRestSiteOptionFinished(RestSiteOption option, bool success, ulong playerId)
    {
        lock (SyncRoot)
        {
            ActiveSelections.Remove(playerId);
        }

        if (!success)
        {
            return;
        }

        RejectLocalPendingOffers(CancelPendingOffersInvolving(playerId));
        TaskHelper.RunSafely(SyncAvailabilityAfterOptionsUpdate(playerId));
    }

    public static void CancelLocalPendingOffers()
    {
        if (!LocalContext.NetId.HasValue)
        {
            return;
        }

        RejectLocalPendingOffers(CancelPendingOffersInvolving(LocalContext.NetId.Value));
    }

    private static async Task SyncAvailabilityAfterOptionsUpdate(ulong playerId)
    {
        await Task.Yield();

        IReadOnlyList<RestSiteOption> options = RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(playerId);
        RejectLocalPendingOffers(MarkUnavailableKindsMissingFromOptions(playerId, options));
    }

    private static List<FriendTradeOffer> MarkUnavailableKindsMissingFromOptions(
        ulong playerId,
        IReadOnlyList<RestSiteOption> options)
    {
        bool cardTradeAvailable = options.Any(option => option.OptionId == FriendCardTradeRestSiteOption.Id);
        bool relicTradeAvailable = options.Any(option => option.OptionId == FriendRelicTradeRestSiteOption.Id);
        List<FriendTradeOffer> canceled = new();

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
                FriendTradeOffer offer = entry.Value.Offer;
                if (!entry.Value.ConfirmationSent &&
                    (IsUnavailable(offer.Sender.NetId, offer.Kind) ||
                     IsUnavailable(offer.Target.NetId, offer.Kind)))
                {
                    canceled.Add(offer);
                }
            }

            foreach (FriendTradeOffer offer in canceled)
            {
                PendingOffers.Remove((offer.Kind, offer.Sender.NetId));
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

        if (!CanCompletePair(first, second) ||
            !CanConsumePendingOffer(counterpart.Offer))
        {
            RejectLocalPendingOffers(CancelPendingOffer(counterpart.Offer));
            return false;
        }

        if (!await ConfirmPendingOffer(counterpart.Offer))
        {
            CancelPendingOffer(counterpart.Offer);
            return false;
        }

        DisableLocalOptionsIfPlayer(first.Sender);
        DisableLocalOptionsIfPlayer(second.Sender);

        if (!await CompletePair(first, second))
        {
            CancelPendingOffer(counterpart.Offer);
            EnableLocalOptionsIfPlayer(counterpart.Offer.Sender);
            return false;
        }

        ConsumePendingOffer(counterpart.Offer);
        return true;
    }

    private static async Task<bool> ConfirmPendingOffer(FriendTradeOffer pendingOffer)
    {
        if (LocalContext.IsMe(pendingOffer.Sender))
        {
            bool accepted;
            bool shouldSend;
            PendingTrade? pendingTrade = null;

            lock (SyncRoot)
            {
                shouldSend = PendingOffers.TryGetValue(
                    (pendingOffer.Kind, pendingOffer.Sender.NetId),
                    out PendingTrade? pending) &&
                    pending.Offer == pendingOffer;
                pendingTrade = pending;

                accepted = shouldSend &&
                           !ActiveSelections.Contains(pendingOffer.Sender.NetId) &&
                           !IsUnavailable(pendingOffer.Sender.NetId, pendingOffer.Kind) &&
                           !IsUnavailable(pendingOffer.Target.NetId, pendingOffer.Kind);

                if (shouldSend)
                {
                    pendingTrade!.ConfirmationSent = true;

                    if (!accepted)
                    {
                        PendingOffers.Remove((pendingOffer.Kind, pendingOffer.Sender.NetId));
                    }
                }
            }

            if (shouldSend)
            {
                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                    pendingOffer.Sender,
                    pendingOffer.ConfirmationChoiceId,
                    PlayerChoiceResult.FromIndex(accepted ? 1 : 0));
            }

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
                ActiveSelections.Contains(offer.Sender.NetId) ||
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

    private static void ConsumePendingOffer(FriendTradeOffer offer)
    {
        lock (SyncRoot)
        {
            if (PendingOffers.TryGetValue((offer.Kind, offer.Sender.NetId), out PendingTrade? pending) &&
                pending.Offer == offer)
            {
                PendingOffers.Remove((offer.Kind, offer.Sender.NetId));
            }
        }

        IReadOnlyList<RestSiteOption> options =
            RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(offer.Sender.NetId);
        if (options is not IList<RestSiteOption> mutableOptions)
        {
            Log.Error("[FriendTrading] Could not consume pending trade option because rest site options were not mutable.");
            return;
        }

        int optionIndex = mutableOptions.IndexOf(offer.Option);
        if (optionIndex < 0)
        {
            Log.Error("[FriendTrading] Could not consume pending trade option because it was no longer available.");
            return;
        }

        offer.Sender.RunState.CurrentMapPointHistoryEntry
            ?.GetEntry(offer.Sender.NetId)
            .RestSiteChoices
            .Add(offer.Option.OptionId);

        if (Hook.ShouldDisableRemainingRestSiteOptions(offer.Sender.RunState, offer.Sender))
        {
            mutableOptions.Clear();
        }
        else
        {
            mutableOptions.RemoveAt(optionIndex);
        }

        RejectLocalPendingOffers(CancelPendingOffersInvolving(offer.Sender.NetId));
        RejectLocalPendingOffers(MarkUnavailableKindsMissingFromOptions(offer.Sender.NetId, options));

        if (LocalContext.IsMe(offer.Sender))
        {
            NRestSiteRoom.Instance?.AfterSelectingOption(offer.Option);
        }
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

    private static List<FriendTradeOffer> CancelPendingOffersInvolving(ulong playerId)
    {
        lock (SyncRoot)
        {
            return CancelPendingOffersInvolvingLocked(playerId);
        }
    }

    private static List<FriendTradeOffer> CancelPendingOffersInvolvingLocked(ulong playerId)
    {
        List<FriendTradeOffer> canceled = PendingOffers
            .Where(entry =>
                !entry.Value.ConfirmationSent &&
                (entry.Value.Offer.Sender.NetId == playerId ||
                 entry.Value.Offer.Target.NetId == playerId))
            .Select(entry => entry.Value.Offer)
            .ToList();

        foreach (FriendTradeOffer offer in canceled)
        {
            PendingOffers.Remove((offer.Kind, offer.Sender.NetId));
        }

        return canceled;
    }

    private static List<FriendTradeOffer> CancelPendingOffersFromSenderLocked(ulong playerId)
    {
        List<FriendTradeOffer> canceled = PendingOffers
            .Where(entry =>
                !entry.Value.ConfirmationSent &&
                entry.Value.Offer.Sender.NetId == playerId)
            .Select(entry => entry.Value.Offer)
            .ToList();

        foreach (FriendTradeOffer offer in canceled)
        {
            PendingOffers.Remove((offer.Kind, offer.Sender.NetId));
        }

        return canceled;
    }

    private static List<FriendTradeOffer> CancelPendingOffer(FriendTradeOffer offer)
    {
        lock (SyncRoot)
        {
            if (PendingOffers.TryGetValue((offer.Kind, offer.Sender.NetId), out PendingTrade? pending) &&
                pending.Offer == offer)
            {
                PendingOffers.Remove((offer.Kind, offer.Sender.NetId));
                return pending.ConfirmationSent
                    ? new List<FriendTradeOffer>()
                    : new List<FriendTradeOffer> { offer };
            }
        }

        return new List<FriendTradeOffer>();
    }

    private static void RejectLocalPendingOffers(IEnumerable<FriendTradeOffer> offers)
    {
        foreach (FriendTradeOffer offer in offers)
        {
            if (!LocalContext.IsMe(offer.Sender))
            {
                continue;
            }

            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                offer.Sender,
                offer.ConfirmationChoiceId,
                PlayerChoiceResult.FromIndex(0));
        }
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

        public bool ConfirmationSent { get; set; }
    }
}
