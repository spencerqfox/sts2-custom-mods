using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using TheFlagellant.Relics;

namespace TheFlagellant.RelicPools;

public sealed class FlagellantRelicPool : RelicPoolModel
{
    public override string EnergyColorName => "ironclad";

    public override Color LabOutlineColor => StsColors.red;

    protected override IEnumerable<RelicModel> GenerateAllRelics()
    {
        return new RelicModel[]
        {
            ModelDb.Relic<FlagellantStarterRelic>(),
            ModelDb.Relic<HallowedRosary>(),
            ModelDb.Relic<DuVuDoll>(),
            ModelDb.Relic<DarkstonePeriapt>(),
            ModelDb.Relic<ScarletLetter>(),
            ModelDb.Relic<QuiltedVestment>(),
            ModelDb.Relic<InquisitorsSeal>(),
            ModelDb.Relic<BloodiedBandages>(),
            ModelDb.Relic<Stigmata>()
        };
    }
}
