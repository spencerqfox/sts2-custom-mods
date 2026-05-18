using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using HeavyEnchantment.Enchantments;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace HeavyEnchantment.Patches;

[HarmonyPatch(typeof(FieldOfManSizedHoles), "GenerateInitialOptions")]
internal static class FieldOfManSizedHolesHeavyOptionPatch
{
    private const string HeavyOptionKey = "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.HEAVY";
    private const string HeavyFinishKey = "FIELD_OF_MAN_SIZED_HOLES.pages.HEAVY.description";

    private static readonly MethodInfo? SetEventFinishedMethod =
        AccessTools.Method(typeof(EventModel), "SetEventFinished", new[] { typeof(LocString) });

    [HarmonyPostfix]
    private static void Postfix(FieldOfManSizedHoles __instance, ref IReadOnlyList<EventOption> __result)
    {
        var owner = __instance.Owner;
        if (owner is null || __result.Any(static option => option.TextKey == HeavyOptionKey))
        {
            return;
        }

        if (!owner.Deck.Cards.Any(ModelDb.Enchantment<Heavy>().CanEnchant))
        {
            return;
        }

        var options = __result.ToList();
        options.Add(new EventOption(__instance, () => OnChosen(__instance), HeavyOptionKey, HoverTipFactory.FromEnchantment<Heavy>()));
        __result = options;
    }

    private static async Task OnChosen(FieldOfManSizedHoles field)
    {
        var owner = field.Owner;
        if (owner is null)
        {
            return;
        }

        CardModel? card = (await CardSelectCmd.FromDeckForEnchantment(
            owner,
            ModelDb.Enchantment<Heavy>(),
            1,
            new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1))).FirstOrDefault();

        if (card != null)
        {
            CardCmd.Enchant<Heavy>(card, 1m);
            NCardEnchantVfx? vfx = NCardEnchantVfx.Create(card);
            if (vfx != null)
            {
                NRun.Instance?.GlobalUi.CardPreviewContainer.AddChildSafely(vfx);
            }
        }

        SetEventFinishedMethod?.Invoke(field, new object[] { new LocString("events", HeavyFinishKey) });
    }
}
