using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace FrozenHand.Patches;

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
public static class NCombatUiActivatePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance, CombatState state)
    {
        var player = LocalContext.GetMe(state);

        if (player?.PlayerCombatState is null)
        {
            return;
        }

        foreach (var card in player.PlayerCombatState.Hand.Cards)
        {
            if (__instance.Hand.GetCardHolder(card) is not null)
            {
                continue;
            }

            var cardNode = NCard.Create(card);

            if (cardNode is null)
            {
                continue;
            }

            __instance.Hand.Add(cardNode);
        }
    }
}
