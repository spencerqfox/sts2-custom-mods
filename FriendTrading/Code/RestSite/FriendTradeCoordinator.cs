using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FriendTrading.RestSite;

internal static class FriendTradeCoordinator
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<(FriendTradeKind Kind, ulong SenderId), PendingTrade> PendingOffers = new();
    private static readonly HashSet<(FriendTradeKind Kind, ulong PlayerId)> UnavailablePlayers = new();

    public static void Reset()
    {
        lock (SyncRoot)
        {
            PendingOffers.Clear();
            UnavailablePlayers.Clear();
        }
    }

    public static bool TryCancelPending(Player sender, FriendTradeKind kind)
    {
        PendingTrade? canceled = null;

        lock (SyncRoot)
        {
            var offerKey = (kind, sender.NetId);
            if (PendingOffers.TryGetValue(offerKey, out canceled))
            {
                PendingOffers.Remove(offerKey);
            }
        }

        canceled?.Resolve(false);
        return canceled != null;
    }

    public static Task<bool> Submit(FriendTradeOffer offer, out bool isPending)
    {
        isPending = false;

        if (!offer.Payload.CanTradeTo(offer.Target))
        {
            return Task.FromResult(false);
        }

        PendingTrade? canceled = null;
        PendingTrade? counterpart = null;
        PendingTrade? pending = null;

        lock (SyncRoot)
        {
            if (IsUnavailable(offer.Sender.NetId, offer.Kind) ||
                IsUnavailable(offer.Target.NetId, offer.Kind))
            {
                return Task.FromResult(false);
            }

            var counterpartKey = (offer.Kind, offer.Target.NetId);
            if (PendingOffers.TryGetValue(counterpartKey, out PendingTrade? waitingOffer) &&
                waitingOffer.Offer.Target.NetId == offer.Sender.NetId)
            {
                PendingOffers.Remove(counterpartKey);
                counterpart = waitingOffer;
            }
            else
            {
                var offerKey = (offer.Kind, offer.Sender.NetId);
                if (PendingOffers.TryGetValue(offerKey, out canceled))
                {
                    PendingOffers.Remove(offerKey);
                }

                pending = new PendingTrade(offer);
                PendingOffers[offerKey] = pending;
                isPending = true;
            }
        }

        canceled?.Resolve(false);

        if (counterpart == null)
        {
            return pending!.Task;
        }

        return CompletePairAndResolve(offer, counterpart);
    }

    public static void OnRestSiteOptionChosen(RestSiteOption option, bool success, ulong playerId)
    {
        if (!success)
        {
            return;
        }

        List<PendingTrade> canceled = new();

        lock (SyncRoot)
        {
            MarkUnavailable(playerId);

            foreach (var entry in PendingOffers)
            {
                FriendTradeOffer offer = entry.Value.Offer;
                if (IsUnavailable(offer.Sender.NetId, offer.Kind) ||
                    IsUnavailable(offer.Target.NetId, offer.Kind))
                {
                    canceled.Add(entry.Value);
                }
            }

            foreach (PendingTrade pending in canceled)
            {
                PendingOffers.Remove((pending.Offer.Kind, pending.Offer.Sender.NetId));
            }
        }

        foreach (PendingTrade pending in canceled)
        {
            pending.Resolve(false);
        }
    }

    private static async Task<bool> CompletePairAndResolve(FriendTradeOffer offer, PendingTrade counterpart)
    {
        DisableLocalOptionsIfPlayer(offer.Sender);
        DisableLocalOptionsIfPlayer(counterpart.Offer.Sender);

        bool success = await CompletePair(offer, counterpart.Offer);
        counterpart.Resolve(success);
        return success;
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

    private static void MarkUnavailable(ulong playerId)
    {
        UnavailablePlayers.Add((FriendTradeKind.Card, playerId));
        UnavailablePlayers.Add((FriendTradeKind.Relic, playerId));
    }

    private static bool IsUnavailable(ulong playerId, FriendTradeKind kind)
    {
        return UnavailablePlayers.Contains((kind, playerId));
    }

    private static void DisableLocalOptionsIfPlayer(Player player)
    {
        if (LocalContext.IsMe(player))
        {
            NRestSiteRoom.Instance?.DisableOptions();
        }
    }

    private sealed class PendingTrade
    {
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingTrade(FriendTradeOffer offer)
        {
            Offer = offer;
        }

        public FriendTradeOffer Offer { get; }

        public Task<bool> Task => _completion.Task;

        public void Resolve(bool success)
        {
            _completion.TrySetResult(success);
        }
    }
}
