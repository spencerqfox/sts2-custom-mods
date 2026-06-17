using Godot;
using MegaCrit.Sts2.Core.Models;
using TheFlagellant.Cards;

namespace TheFlagellant.CardPools;

public sealed class FlagellantCardPool : CardPoolModel
{
    public override string Title => "the_flagellant";

    public override string EnergyColorName => "ironclad";

    public override string CardFrameMaterialPath => "card_frame_red";

    public override Color DeckEntryCardColor => new("B8323D");

    public override Color EnergyOutlineColor => new("5A1118");

    public override bool IsColorless => false;

    protected override CardModel[] GenerateAllCards()
    {
        return new CardModel[]
        {
            ModelDb.Card<FlagellantStrike>(),
            ModelDb.Card<FlagellantDefend>(),
            ModelDb.Card<Flog>(),
            ModelDb.Card<Brace>(),
            ModelDb.Card<Flay>(),
            ModelDb.Card<Whiplash>(),
            ModelDb.Card<Vigilance>(),
            ModelDb.Card<Maledict>(),
            ModelDb.Card<Jinx>(),
            ModelDb.Card<Indulgence>(),
            ModelDb.Card<Sackcloth>(),
            ModelDb.Card<Flail>(),
            ModelDb.Card<Lament>(),
            ModelDb.Card<Confession>(),
            ModelDb.Card<Fervor>(),
            ModelDb.Card<ToughSkin>(),
            ModelDb.Card<Litany>(),
            ModelDb.Card<Revelation>(),
            ModelDb.Card<Catharsis>(),
            ModelDb.Card<Mantra>(),
            ModelDb.Card<Catechism>(),
            ModelDb.Card<Tribulation>(),
            ModelDb.Card<Devotion>(),
            ModelDb.Card<Judgement>(),
            ModelDb.Card<Welts>(),
            ModelDb.Card<Mortify>(),
            ModelDb.Card<Reprisal>(),
            ModelDb.Card<Rebuke>(),
            ModelDb.Card<Relapse>(),
            ModelDb.Card<Overreach>(),
            ModelDb.Card<DeepPrayer>(),
            ModelDb.Card<WeakGrip>(),
            ModelDb.Card<Retribution>(),
            ModelDb.Card<Premonition>(),
            ModelDb.Card<SharedSuffering>(),
            ModelDb.Card<CloakOfSins>(),
            ModelDb.Card<Renounce>(),
            ModelDb.Card<Galvanize>(),
            ModelDb.Card<Flinch>(),
            ModelDb.Card<Mania>(),
            ModelDb.Card<Sympathy>(),
            ModelDb.Card<Discipline>(),
            ModelDb.Card<Lucidity>(),
            ModelDb.Card<Indignation>(),
            ModelDb.Card<Delirium>(),
            ModelDb.Card<Cull>(),
            ModelDb.Card<Grit>(),
            ModelDb.Card<BreakingPoint>(),
            ModelDb.Card<Outburst>(),
            ModelDb.Card<Aggravate>(),
            ModelDb.Card<Onslaught>(),
            ModelDb.Card<Defiance>(),
            ModelDb.Card<Conviction>(),
            ModelDb.Card<ResoluteStrike>(),
            ModelDb.Card<BolsteringBlow>(),
            ModelDb.Card<Uprising>(),
            ModelDb.Card<Desperation>(),
            ModelDb.Card<Reinforce>(),
            ModelDb.Card<Regroup>(),
            ModelDb.Card<Gird>(),
            ModelDb.Card<Condescend>(),
            ModelDb.Card<Mutter>(),
            ModelDb.Card<Lacerate>(),
            ModelDb.Card<LayBare>(),
            ModelDb.Card<Extoll>(),
            ModelDb.Card<Rapture>(),
            ModelDb.Card<Compulsion>(),
            ModelDb.Card<Exorcise>(),
            ModelDb.Card<Maelstrom>(),
            ModelDb.Card<Anathema>(),
            ModelDb.Card<Redemption>(),
            ModelDb.Card<Reckoning>(),
            ModelDb.Card<Exaltation>(),
            ModelDb.Card<CrownOfThorns>(),
            ModelDb.Card<Martyr>(),
            ModelDb.Card<Smite>(),
            ModelDb.Card<Triumph>(),
            ModelDb.Card<Perseverance>(),
            ModelDb.Card<Manifestation>(),
            ModelDb.Card<Vow>(),
            ModelDb.Card<Proselytize>(),
            ModelDb.Card<Fortitude>(),
            ModelDb.Card<Consumption>(),
            ModelDb.Card<TranscendentForm>(),
            ModelDb.Card<Tenderize>(),
            ModelDb.Card<OriginalSin>()
        };
    }
}
