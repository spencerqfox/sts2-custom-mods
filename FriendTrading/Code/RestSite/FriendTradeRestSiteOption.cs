using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Runs;

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

        bool isReciprocal = await FriendTradeCoordinator.SelectSubmissionIntent(Kind, Owner, target);
        uint confirmationChoiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(Owner);
        return await FriendTradeCoordinator.Submit(
            new FriendTradeOffer(Kind, this, Owner, target, payload, isReciprocal, confirmationChoiceId));
    }
}
