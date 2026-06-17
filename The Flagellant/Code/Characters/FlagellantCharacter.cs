using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using TheFlagellant.CardPools;
using TheFlagellant.Cards;
using TheFlagellant.PotionPools;
using TheFlagellant.RelicPools;
using TheFlagellant.Relics;

namespace TheFlagellant.Characters;

public sealed class FlagellantCharacter : CharacterModel
{
    public override Color NameColor => new("B8323D");

    public override CharacterGender Gender => CharacterGender.Neutral;

    protected override CharacterModel? UnlocksAfterRunAs => null;

    public override int StartingHp => 72;

    public override int StartingGold => 99;

    public override CardPoolModel CardPool => ModelDb.CardPool<FlagellantCardPool>();

    public override RelicPoolModel RelicPool => ModelDb.RelicPool<FlagellantRelicPool>();

    public override PotionPoolModel PotionPool => ModelDb.PotionPool<FlagellantPotionPool>();

    public override IEnumerable<CardModel> StartingDeck => new CardModel[]
    {
        ModelDb.Card<FlagellantStrike>(),
        ModelDb.Card<FlagellantStrike>(),
        ModelDb.Card<FlagellantStrike>(),
        ModelDb.Card<FlagellantStrike>(),
        ModelDb.Card<FlagellantDefend>(),
        ModelDb.Card<FlagellantDefend>(),
        ModelDb.Card<FlagellantDefend>(),
        ModelDb.Card<FlagellantDefend>(),
        ModelDb.Card<Flog>(),
        ModelDb.Card<Brace>()
    };

    public override IReadOnlyList<RelicModel> StartingRelics => new RelicModel[]
    {
        ModelDb.Relic<FlagellantStarterRelic>()
    };

    public override float AttackAnimDelay => 0.15f;

    public override float CastAnimDelay => 0.25f;

    public override Color EnergyLabelOutlineColor => new("5A1118FF");

    public override Color DialogueColor => new("4A1116");

    public override VfxColor SpeechBubbleColor => VfxColor.Red;

    public override Color MapDrawingColor => new("9D252F");

    public override Color RemoteTargetingLineColor => new("D4555EFF");

    public override Color RemoteTargetingLineOutline => new("5A1118FF");

    public override string CharacterSelectSfx => "event:/sfx/characters/ironclad/ironclad_select";

    public override string CharacterTransitionSfx => "event:/sfx/ui/wipe_ironclad";

    protected override string CharacterSelectIconPath =>
        ImageHelper.GetImagePath("characters/flagellantcharactershot.png");

    protected override string CharacterSelectLockedIconPath =>
        ImageHelper.GetImagePath("characters/flagellantcharactershot.png");

    protected override string IconPath => SceneHelper.GetScenePath("ui/character_icons/ironclad_icon");

    public override List<string> GetArchitectAttackVfx()
    {
        return new List<string>
        {
            "vfx/vfx_attack_blunt",
            "vfx/vfx_heavy_blunt",
            "vfx/vfx_attack_slash",
            "vfx/vfx_bloody_impact",
            "vfx/vfx_chain"
        };
    }
}
