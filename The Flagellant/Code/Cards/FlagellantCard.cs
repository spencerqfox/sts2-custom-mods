using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace TheFlagellant.Cards;

public abstract class FlagellantCard : CardModel
{
    protected FlagellantCard(int canonicalEnergyCost, CardType type, CardRarity rarity, TargetType targetType)
        : base(canonicalEnergyCost, type, rarity, targetType)
    {
    }

    // Cards look for art at images/cards/flagellant<classname>.png by default.
    // Override this when a card needs a custom filename.
    protected virtual string? CardArtImagePath => ImageHelper.GetImagePath($"cards/{DefaultCardArtFileName}.png");

    private string DefaultCardArtFileName
    {
        get
        {
            string name = GetType().Name;
            return (name.StartsWith("Flagellant", StringComparison.Ordinal) ? name : $"Flagellant{name}").ToLowerInvariant();
        }
    }

    private string ResolvedPortraitPath =>
        CardArtImagePath is { } path && ResourceLoader.Exists(path) ? path : MissingPortraitPath;

    public override string PortraitPath => ResolvedPortraitPath;

    public override string BetaPortraitPath => ResolvedPortraitPath;

    public override IEnumerable<string> AllPortraitPaths => new[]
    {
        ResolvedPortraitPath
    };
}
