using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;

namespace FriendTrading.RestSite;

internal sealed class FriendTradeOffer
{
    public FriendTradeOffer(
        FriendTradeKind kind,
        RestSiteOption option,
        Player sender,
        Player target,
        IFriendTradePayload payload,
        bool isReciprocal,
        uint confirmationChoiceId)
    {
        Kind = kind;
        Option = option;
        Sender = sender;
        Target = target;
        Payload = payload;
        IsReciprocal = isReciprocal;
        ConfirmationChoiceId = confirmationChoiceId;
    }

    public FriendTradeKind Kind { get; }

    public RestSiteOption Option { get; }

    public Player Sender { get; }

    public Player Target { get; }

    public IFriendTradePayload Payload { get; }

    public bool IsReciprocal { get; }

    public uint ConfirmationChoiceId { get; }

    public FriendTradeWaitingScreen? WaitingScreen { get; set; }
}
