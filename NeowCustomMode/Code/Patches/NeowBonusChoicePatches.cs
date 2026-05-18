using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;

namespace NeowCustomMode.Patches;

internal static class NeowBonusChoices
{
    private const string NeowBonusEntry = "NEOW_BONUS";
    private const string PositiveDoneDescriptionKey = "NEOW.pages.DONE.POSITIVE.description";
    private const string CursedDoneDescriptionKey = "NEOW.pages.DONE.CURSED.description";

    private static readonly AccessTools.FieldRef<Neow, List<EventOption>?> ModifierOptionsRef =
        AccessTools.FieldRefAccess<Neow, List<EventOption>?>("_modifierOptions");

    private static readonly MethodInfo SetEventStateMethod =
        AccessTools.Method(
            typeof(EventModel),
            "SetEventState",
            new[] { typeof(LocString), typeof(IEnumerable<EventOption>) })
        ?? throw new MissingMethodException(nameof(EventModel), "SetEventState");

    private static readonly MethodInfo SetEventFinishedMethod =
        AccessTools.Method(typeof(EventModel), "SetEventFinished", new[] { typeof(LocString) })
        ?? throw new MissingMethodException(nameof(EventModel), "SetEventFinished");

    public static bool TryReplaceMarkerWithChoices(
        Neow neow,
        IReadOnlyList<EventOption> currentOptions,
        out IReadOnlyList<EventOption> replacementOptions)
    {
        replacementOptions = currentOptions;
        if (!IsSingleNeowBonusMarker(currentOptions) || !TryGetMarkerIndex(neow, out int markerIndex))
        {
            return false;
        }

        IReadOnlyList<EventOption> choices = CreateStandardChoices(neow, markerIndex);
        if (choices.Count == 0)
        {
            return false;
        }

        replacementOptions = choices;
        return true;
    }

    public static void ReplaceCurrentMarkerWithChoices(Neow neow)
    {
        if (TryReplaceMarkerWithChoices(neow, neow.CurrentOptions, out IReadOnlyList<EventOption> choices))
        {
            SetEventState(neow, neow.InitialDescription, choices);
        }
    }

    private static bool IsSingleNeowBonusMarker(IReadOnlyList<EventOption> options)
    {
        return options.Count == 1 && options[0].TextKey == NeowBonusEntry;
    }

    private static bool TryGetMarkerIndex(Neow neow, out int markerIndex)
    {
        List<EventOption>? modifierOptions = ModifierOptionsRef(neow);
        if (modifierOptions == null)
        {
            markerIndex = -1;
            return false;
        }

        markerIndex = modifierOptions.FindIndex(static option => option.TextKey == NeowBonusEntry);
        return markerIndex >= 0;
    }

    private static IReadOnlyList<EventOption> CreateStandardChoices(Neow neow, int markerIndex)
    {
        Player player = neow.Owner!;
        List<EventOption> curseOptions = CreateCurseOptions(neow, player, markerIndex).ToList();
        if (ScrollBoxes.CanGenerateBundles(player))
        {
            curseOptions.Add(CreateRelicOption<ScrollBoxes>(neow, player, markerIndex, CursedDoneDescriptionKey));
        }

        curseOptions.RemoveAll(option => IsDisallowed(option, player));
        if (curseOptions.Count == 0)
        {
            return Array.Empty<EventOption>();
        }

        EventOption curseOption = neow.Rng.NextItem(curseOptions)!;
        List<EventOption> positiveOptions = CreatePositiveOptions(neow, player, markerIndex).ToList();
        RemoveConflictingPositiveOptions(curseOption, positiveOptions);

        if (curseOption.Relic is not LargeCapsule)
        {
            positiveOptions.Add(neow.Rng.NextBool()
                ? CreateRelicOption<LavaRock>(neow, player, markerIndex, PositiveDoneDescriptionKey)
                : CreateRelicOption<SmallCapsule>(neow, player, markerIndex, PositiveDoneDescriptionKey));
        }

        positiveOptions.Add(neow.Rng.NextBool()
            ? CreateRelicOption<NutritiousOyster>(neow, player, markerIndex, PositiveDoneDescriptionKey)
            : CreateRelicOption<StoneHumidifier>(neow, player, markerIndex, PositiveDoneDescriptionKey));

        positiveOptions.Add(neow.Rng.NextBool()
            ? CreateRelicOption<NeowsTalisman>(neow, player, markerIndex, PositiveDoneDescriptionKey)
            : CreateRelicOption<Pomander>(neow, player, markerIndex, PositiveDoneDescriptionKey));

        positiveOptions.RemoveAll(option => IsDisallowed(option, player));

        List<EventOption> choices = positiveOptions
            .UnstableShuffle(neow.Rng)
            .Take(2)
            .ToList();
        choices.Add(curseOption);
        return choices;
    }

    private static IEnumerable<EventOption> CreatePositiveOptions(Neow neow, Player player, int markerIndex)
    {
        yield return CreateRelicOption<ArcaneScroll>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<BoomingConch>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<GoldenPearl>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<LeadPaperweight>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<LostCoffer>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<NeowsTorment>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<NewLeaf>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<PreciseScissors>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<PhialHolster>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<WingedBoots>(neow, player, markerIndex, PositiveDoneDescriptionKey);
        yield return CreateRelicOption<MassiveScroll>(neow, player, markerIndex, PositiveDoneDescriptionKey);
    }

    private static IEnumerable<EventOption> CreateCurseOptions(Neow neow, Player player, int markerIndex)
    {
        yield return CreateRelicOption<CursedPearl>(neow, player, markerIndex, CursedDoneDescriptionKey);
        yield return CreateRelicOption<HeftyTablet>(neow, player, markerIndex, CursedDoneDescriptionKey);
        yield return CreateRelicOption<LargeCapsule>(neow, player, markerIndex, CursedDoneDescriptionKey);
        yield return CreateRelicOption<LeafyPoultice>(neow, player, markerIndex, CursedDoneDescriptionKey);
        yield return CreateRelicOption<PrecariousShears>(neow, player, markerIndex, CursedDoneDescriptionKey);
        yield return CreateRelicOption<SilverCrucible>(neow, player, markerIndex, CursedDoneDescriptionKey);
        yield return CreateRelicOption<NeowsBones>(neow, player, markerIndex, PositiveDoneDescriptionKey);
    }

    private static EventOption CreateRelicOption<T>(
        Neow neow,
        Player player,
        int markerIndex,
        string doneDescriptionKey)
        where T : RelicModel
    {
        RelicModel relic = ModelDb.Relic<T>().ToMutable();
        relic.Owner = player;
        string textKey = $"NEOW.pages.INITIAL.options.{relic.Id.Entry}";
        return EventOption.FromRelic(
            relic,
            neow,
            () => OnRelicChosen(neow, relic, markerIndex, doneDescriptionKey),
            textKey);
    }

    private static async Task OnRelicChosen(
        Neow neow,
        RelicModel relic,
        int markerIndex,
        string doneDescriptionKey)
    {
        await RelicCmd.Obtain(relic, neow.Owner!);
        ContinueAfterNeowBonus(neow, markerIndex, doneDescriptionKey);
    }

    private static void ContinueAfterNeowBonus(Neow neow, int markerIndex, string doneDescriptionKey)
    {
        List<EventOption>? modifierOptions = ModifierOptionsRef(neow);
        if (modifierOptions != null && markerIndex + 1 < modifierOptions.Count)
        {
            SetEventState(neow, neow.InitialDescription, new[] { modifierOptions[markerIndex + 1] });
            return;
        }

        SetEventFinished(neow, new LocString("ancients", doneDescriptionKey));
    }

    private static bool IsDisallowed(EventOption option, Player player)
    {
        return option.Relic != null && !option.Relic.IsAllowed(player.RunState);
    }

    private static void RemoveConflictingPositiveOptions(EventOption curseOption, List<EventOption> positiveOptions)
    {
        if (curseOption.Relic is CursedPearl)
        {
            positiveOptions.RemoveAll(static option => option.Relic is GoldenPearl);
        }

        if (curseOption.Relic is HeftyTablet)
        {
            positiveOptions.RemoveAll(static option => option.Relic is ArcaneScroll);
        }

        if (curseOption.Relic is LeafyPoultice)
        {
            positiveOptions.RemoveAll(static option => option.Relic is NewLeaf);
        }

        if (curseOption.Relic is PrecariousShears)
        {
            positiveOptions.RemoveAll(static option => option.Relic is PreciseScissors);
        }
    }

    private static void SetEventState(Neow neow, LocString description, IEnumerable<EventOption> eventOptions)
    {
        SetEventStateMethod.Invoke(neow, new object[] { description, eventOptions });
    }

    private static void SetEventFinished(Neow neow, LocString description)
    {
        SetEventFinishedMethod.Invoke(neow, new object[] { description });
    }
}

[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
public static class NeowGenerateInitialOptionsPatch
{
    public static void Postfix(Neow __instance, ref IReadOnlyList<EventOption> __result)
    {
        if (NeowBonusChoices.TryReplaceMarkerWithChoices(__instance, __result, out IReadOnlyList<EventOption> choices))
        {
            __result = choices;
        }
    }
}

[HarmonyPatch(typeof(Neow), "OnModifierOptionSelected")]
public static class NeowOnModifierOptionSelectedPatch
{
    public static void Postfix(Neow __instance, ref Task __result)
    {
        __result = ReplaceMarkerAfterOriginal(__instance, __result);
    }

    private static async Task ReplaceMarkerAfterOriginal(Neow neow, Task original)
    {
        await original;
        NeowBonusChoices.ReplaceCurrentMarkerWithChoices(neow);
    }
}
