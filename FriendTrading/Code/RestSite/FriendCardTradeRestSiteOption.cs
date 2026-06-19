using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace FriendTrading.RestSite;

internal sealed class FriendCardTradeRestSiteOption : FriendTradeRestSiteOption
{
    public const string Id = "FRIEND_TRADE_CARD";
    private const string IconPath = "res://images/ui/rest_site/option_friend_trade_card.png";

    public FriendCardTradeRestSiteOption(Player owner)
        : base(owner)
    {
    }

    public override string OptionId => Id;

    public override IEnumerable<string> AssetPaths => new[] { IconPath };

    protected override FriendTradeKind Kind => FriendTradeKind.Card;

    public static bool CanTradeCard(CardModel card)
    {
        return card.Pile?.Type == PileType.Deck && card.IsRemovable;
    }

    protected override async Task<IFriendTradePayload?> SelectPayload(Player target)
    {
        List<CardModel> deckOrder = Owner.Deck.Cards.ToList();
        var prefs = new CardSelectorPrefs(new LocString("card_selection", "TO_TRADE_CARD"), 1)
        {
            RequireManualConfirmation = true,
            Cancelable = true
        };

        CardModel? card = (await CardSelectCmd.FromDeckGeneric(
            Owner,
            prefs,
            CanTradeCard,
            deckOrder.IndexOf)).FirstOrDefault();

        return card == null ? null : new CardTradePayload(card);
    }

    private sealed class CardTradePayload : IFriendTradePayload
    {
        private readonly Player _sourceOwner;
        private readonly CardModel _sourceCard;
        private readonly MegaCrit.Sts2.Core.Saves.Runs.SerializableCard _serializedCard;
        private CardModel? _targetCard;
        private bool _sourceRemoved;

        public CardTradePayload(CardModel sourceCard)
        {
            _sourceOwner = sourceCard.Owner;
            _sourceCard = sourceCard;
            _serializedCard = sourceCard.ToSerializable();
        }

        public bool CanTradeTo(Player target)
        {
            return target.Creature.IsAlive &&
                   _sourceOwner.Deck.Cards.Contains(_sourceCard) &&
                   CanTradeCard(_sourceCard);
        }

        public async Task RemoveFromSource()
        {
            await CardPileCmd.RemoveFromDeck(_sourceCard, showPreview: false);
            _sourceRemoved = true;
        }

        public async Task RestoreToSource()
        {
            if (!_sourceRemoved)
            {
                return;
            }

            CardModel restored = _sourceOwner.RunState.LoadCard(_serializedCard, _sourceOwner);
            var result = await CardPileCmd.Add(
                restored,
                PileType.Deck,
                CardPilePosition.Bottom,
                clonedBy: null,
                skipVisuals: false);

            if (!result.success)
            {
                restored.RemoveFromState();
                throw new InvalidOperationException("Failed to restore traded card to the source deck.");
            }

            _sourceRemoved = false;
        }

        public async Task AddToTarget(Player target)
        {
            CardModel incoming = target.RunState.LoadCard(_serializedCard, target);
            _targetCard = incoming;
            var result = await CardPileCmd.Add(
                incoming,
                PileType.Deck,
                CardPilePosition.Bottom,
                clonedBy: null,
                skipVisuals: false);

            if (!result.success)
            {
                incoming.RemoveFromState();
                _targetCard = null;
                throw new InvalidOperationException("Failed to add traded card to the target deck.");
            }
        }

        public async Task RemoveFromTarget()
        {
            if (_targetCard == null)
            {
                return;
            }

            if (_targetCard.Pile?.Type == PileType.Deck)
            {
                await CardPileCmd.RemoveFromDeck(_targetCard, showPreview: false);
            }
            else if (!_targetCard.HasBeenRemovedFromState)
            {
                _targetCard.RemoveFromState();
            }

            _targetCard = null;
        }
    }
}
