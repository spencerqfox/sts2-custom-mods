using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;

namespace FriendTrading.RestSite;

internal interface IFriendTradePayload
{
    bool CanTradeTo(Player target);

    Task RemoveFromSource();

    Task RestoreToSource();

    Task AddToTarget(Player target);

    Task RemoveFromTarget();
}
