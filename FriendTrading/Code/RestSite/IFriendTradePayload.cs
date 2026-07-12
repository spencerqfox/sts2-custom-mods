using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace FriendTrading.RestSite;

internal interface IFriendTradePayload
{
    AbstractModel TradeItem { get; }

    bool CanTradeTo(Player target);

    Task RemoveFromSource();

    Task RestoreToSource();

    Task AddToTarget(Player target);

    Task RemoveFromTarget();
}
