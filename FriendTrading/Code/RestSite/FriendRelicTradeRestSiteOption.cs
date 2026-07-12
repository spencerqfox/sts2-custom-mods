using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace FriendTrading.RestSite;

internal sealed class FriendRelicTradeRestSiteOption : FriendTradeRestSiteOption
{
    public const string Id = "FRIEND_TRADE_RELIC";
    private const string IconPath = "res://images/ui/rest_site/option_friend_trade_relic.png";
    private const string RelicHolderPath = "res://scenes/relics/relic_basic_holder.tscn";

    public FriendRelicTradeRestSiteOption(Player owner)
        : base(owner)
    {
    }

    public override string OptionId => Id;

    public override IEnumerable<string> AssetPaths => new[] { IconPath, RelicHolderPath };

    protected override FriendTradeKind Kind => FriendTradeKind.Relic;

    public static bool CanTradeRelic(RelicModel relic)
    {
        return relic.IsTradable && !relic.HasUponPickupEffect;
    }

    public static bool CanTradeRelicTo(RelicModel relic, Player target)
    {
        if (!CanTradeRelic(relic))
        {
            return false;
        }

        return relic.IsStackable || target.GetRelicById(relic.Id) == null;
    }

    protected override async Task<IFriendTradePayload?> SelectPayload(Player target)
    {
        List<RelicModel> relics = Owner.Relics
            .Where(relic => CanTradeRelicTo(relic, target))
            .ToList();

        if (relics.Count == 0)
        {
            return null;
        }

        RelicModel? relic = await FriendRelicTradeSelectionScreen.Select(Owner, relics);
        return relic == null ? null : new RelicTradePayload(relic);
    }

    private sealed class RelicTradePayload : IFriendTradePayload
    {
        private readonly Player _sourceOwner;
        private readonly RelicModel _sourceRelic;
        private readonly MegaCrit.Sts2.Core.Saves.Runs.SerializableRelic _serializedRelic;
        private RelicModel? _targetRelic;
        private bool _sourceRemoved;

        public RelicTradePayload(RelicModel sourceRelic)
        {
            _sourceOwner = sourceRelic.Owner;
            _sourceRelic = sourceRelic;
            _serializedRelic = sourceRelic.ToSerializable();
        }

        public AbstractModel TradeItem => _sourceRelic;

        public bool CanTradeTo(Player target)
        {
            return target.Creature.IsAlive &&
                   _sourceOwner.Relics.Contains(_sourceRelic) &&
                   CanTradeRelicTo(_sourceRelic, target);
        }

        public async Task RemoveFromSource()
        {
            _sourceRemoved = true;
            await RelicCmd.Remove(_sourceRelic);
        }

        public async Task RestoreToSource()
        {
            if (!_sourceRemoved)
            {
                return;
            }

            if (_sourceOwner.Relics.Contains(_sourceRelic))
            {
                _sourceRemoved = false;
                return;
            }

            await RelicCmd.Obtain(RelicModel.FromSerializable(_serializedRelic), _sourceOwner);
            _sourceRemoved = false;
        }

        public async Task AddToTarget(Player target)
        {
            _targetRelic = RelicModel.FromSerializable(_serializedRelic);
            await RelicCmd.Obtain(_targetRelic, target);
        }

        public async Task RemoveFromTarget()
        {
            if (_targetRelic == null)
            {
                return;
            }

            if (_targetRelic.Owner.Relics.Contains(_targetRelic))
            {
                await RelicCmd.Remove(_targetRelic);
            }

            _targetRelic = null;
        }
    }
}
