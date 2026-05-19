using MegaCrit.Sts2.Core.Entities.Players;

namespace FriendTrading.RestSite;

internal sealed class FriendTradeOffer
{
    public FriendTradeOffer(FriendTradeKind kind, Player sender, Player target, IFriendTradePayload payload)
    {
        Kind = kind;
        Sender = sender;
        Target = target;
        Payload = payload;
    }

    public FriendTradeKind Kind { get; }

    public Player Sender { get; }

    public Player Target { get; }

    public IFriendTradePayload Payload { get; }
}
