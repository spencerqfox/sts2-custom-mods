using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using TheFlagellant.Powers;

namespace TheFlagellant.Relics;

public sealed class FlagellantStarterRelic : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override string IconBaseName => "burning_blood";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<ResiliencePower>(1m)
    };

    public override async Task BeforeCombatStart()
    {
        Flash();
        await PowerCmd.Apply<ResiliencePower>(Owner.Creature, DynamicVars["ResiliencePower"].BaseValue, Owner.Creature, null);
    }
}
