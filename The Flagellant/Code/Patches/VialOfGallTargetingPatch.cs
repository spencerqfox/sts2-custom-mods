using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using TheFlagellant.Potions;

namespace TheFlagellant.Patches;

[HarmonyPatch]
internal static class VialOfGallTargetingPatch
{
    private static bool _isTargetingVialOfGall;

    [HarmonyPatch(typeof(NPotionHolder), nameof(NPotionHolder.UsePotion))]
    [HarmonyPrefix]
    private static bool UsePotionPrefix(NPotionHolder __instance, ref Task __result)
    {
        if (__instance.Potion?.Model is not VialOfGall)
        {
            return true;
        }

        __result = UseVialOfGall(__instance);
        return false;
    }

    [HarmonyPatch(typeof(NTargetManager), "AllowedToTargetCreature")]
    [HarmonyPrefix]
    private static bool AllowedToTargetCreaturePrefix(Creature creature, ref bool __result)
    {
        if (!_isTargetingVialOfGall)
        {
            return true;
        }

        __result = !creature.IsDead;
        return false;
    }

    private static async Task UseVialOfGall(NPotionHolder holder)
    {
        if (holder.Potion?.Model is not VialOfGall potion)
        {
            return;
        }

        RunManager.Instance.HoveredModelTracker.OnLocalPotionSelected(potion);
        try
        {
            await TargetAnyCombatCreature(holder, potion);
        }
        finally
        {
            RunManager.Instance.HoveredModelTracker.OnLocalPotionDeselected();
            holder.TryGrabFocus();
        }
    }

    private static async Task TargetAnyCombatCreature(NPotionHolder holder, VialOfGall potion)
    {
        Vector2 startPosition = holder.GlobalPosition + Vector2.Right * holder.Size.X * 0.5f + Vector2.Down * 50f;
        NTargetManager targetManager = NTargetManager.Instance;
        bool isUsingController = NControllerManager.Instance?.IsUsingController == true;

        _isTargetingVialOfGall = true;
        try
        {
            targetManager.StartTargeting(
                TargetType.AnyEnemy,
                startPosition,
                isUsingController ? TargetMode.Controller : TargetMode.ClickMouseToTarget,
                () => ShouldCancelTargeting(potion),
                null);

            if (isUsingController)
            {
                RestrictControllerNavigationToLivingCreatures(potion.Owner.Creature);
            }

            Node? node = await targetManager.SelectionFinished();
            if (node == null)
            {
                return;
            }

            Creature? target = NodeToCreature(node);
            if (target == null)
            {
                throw new ArgumentOutOfRangeException(nameof(node), node, null);
            }

            potion.EnqueueManualUse(target);
        }
        finally
        {
            _isTargetingVialOfGall = false;
            NCombatRoom.Instance?.EnableControllerNavigation();
            NRun.Instance?.GlobalUi?.MultiplayerPlayerContainer.UnlockNavigation();
        }
    }

    private static void RestrictControllerNavigationToLivingCreatures(Creature owner)
    {
        CombatState? combatState = owner.CombatState;
        NCombatRoom? room = NCombatRoom.Instance;
        if (combatState == null || room == null)
        {
            return;
        }

        List<Control> hitboxes = combatState.Creatures
            .Where(creature => creature.IsAlive)
            .Select(creature => room.GetCreatureNode(creature)?.Hitbox)
            .OfType<Control>()
            .ToList();
        if (hitboxes.Count == 0)
        {
            return;
        }

        room.RestrictControllerNavigation(hitboxes);
        hitboxes[0].TryGrabFocus();
    }

    private static bool ShouldCancelTargeting(PotionModel potion)
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return true;
        }

        return potion.IsQueued ||
            potion.Owner.Creature.IsDead ||
            (NOverlayStack.Instance?.ScreenCount ?? 0) > 0 ||
            (NCapstoneContainer.Instance?.InUse ?? false);
    }

    private static Creature? NodeToCreature(Node node)
    {
        return node switch
        {
            NCreature creature => creature.Entity,
            NMultiplayerPlayerState playerState => playerState.Player.Creature,
            _ => null
        };
    }
}
