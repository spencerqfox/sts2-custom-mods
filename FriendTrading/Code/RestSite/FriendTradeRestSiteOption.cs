using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FriendTrading.RestSite;

internal abstract class FriendTradeRestSiteOption : RestSiteOption
{
    protected FriendTradeRestSiteOption(Player owner)
        : base(owner)
    {
    }

    protected abstract FriendTradeKind Kind { get; }

    protected abstract Task<IFriendTradePayload?> SelectPayload(Player target);

    public override async Task<bool> OnSelect()
    {
        if (FriendTradeCoordinator.TryCancelPending(Owner, Kind))
        {
            return false;
        }

        Player? target = await FriendTradeTargetSelector.SelectTarget(Owner, this);
        if (target == null)
        {
            return false;
        }

        IFriendTradePayload? payload = await SelectPayload(target);
        if (payload == null)
        {
            return false;
        }

        Task<bool> result = FriendTradeCoordinator.Submit(
            new FriendTradeOffer(Kind, Owner, target, payload),
            out bool isPending);

        if (isPending && LocalContext.IsMe(Owner))
        {
            NRestSiteRoom.Instance?.GetButtonForOption(this)?.Enable();
        }

        return await result;
    }
}
