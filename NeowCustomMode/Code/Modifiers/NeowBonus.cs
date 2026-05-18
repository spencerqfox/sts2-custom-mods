using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace NeowCustomMode.Modifiers;

public sealed class NeowBonus : ModifierModel
{
    protected override string IconPath => ImageHelper.GetImagePath("packed/map/ancients/ancient_node_neow.png");

    public override Func<Task> GenerateNeowOption(EventModel eventModel)
    {
        return () => Task.CompletedTask;
    }
}
