using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using TheFlagellant.Potions;

namespace TheFlagellant.PotionPools;

public sealed class FlagellantPotionPool : PotionPoolModel
{
    public override string EnergyColorName => "ironclad";

    public override Color LabOutlineColor => StsColors.red;

    protected override IEnumerable<PotionModel> GenerateAllPotions()
    {
        return new PotionModel[]
        {
            ModelDb.Potion<HolyWater>(),
            ModelDb.Potion<VialOfGall>(),
            ModelDb.Potion<BottledSin>()
        };
    }
}
