using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace FriendTrading.RestSite;

internal static class FriendTradeTargetSelector
{
    public static async Task<Player?> SelectTarget(Player owner, RestSiteOption option)
    {
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(owner);

        if (LocalContext.IsMe(owner))
        {
            Player? target = await SelectLocalTarget(owner, option);
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                owner,
                choiceId,
                PlayerChoiceResult.FromPlayerId(target?.NetId));
            return target;
        }

        ulong? playerId = (await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(owner, choiceId))
            .AsPlayerId();
        return playerId.HasValue ? owner.RunState.GetPlayer(playerId.Value) : null;
    }

    private static async Task<Player?> SelectLocalTarget(Player owner, RestSiteOption option)
    {
        NRestSiteRoom room = NRestSiteRoom.Instance!;
        room.AnimateDescriptionDown();

        NRestSiteButton? button = room.GetButtonForOption(option);
        Vector2 startPosition = button != null
            ? button.GlobalPosition + button.Size / 2f
            : room.GlobalPosition + room.Size / 2f;

        bool usingController = NControllerManager.Instance!.IsUsingController;
        NTargetManager targetManager = NTargetManager.Instance!;
        targetManager.StartTargeting(
            TargetType.AnyPlayer,
            startPosition,
            usingController ? TargetMode.Controller : TargetMode.ClickMouseToTarget,
            ShouldCancelTargeting,
            node => AllowHoveringNode(owner, node));

        List<NRestSiteCharacter> focusTargets = new();
        if (usingController)
        {
            focusTargets = room.characterAnims.Where(character => character.Player != owner).ToList();
            for (int i = 0; i < focusTargets.Count; i++)
            {
                Control hitbox = focusTargets[i].Hitbox;
                hitbox.SetFocusMode(Control.FocusModeEnum.All);
                hitbox.FocusNeighborTop = hitbox.GetPath();
                hitbox.FocusNeighborBottom = hitbox.GetPath();
                hitbox.FocusNeighborLeft = i <= 0
                    ? focusTargets[focusTargets.Count - 1].Hitbox.GetPath()
                    : focusTargets[i - 1].Hitbox.GetPath();
                hitbox.FocusNeighborRight = i < focusTargets.Count - 1
                    ? focusTargets[i + 1].Hitbox.GetPath()
                    : focusTargets[0].Hitbox.GetPath();
            }

            focusTargets.FirstOrDefault()?.Hitbox.TryGrabFocus();
        }

        try
        {
            return NodeToPlayer(await targetManager.SelectionFinished());
        }
        finally
        {
            room.AnimateDescriptionUp();
            foreach (NRestSiteCharacter character in focusTargets)
            {
                character.Hitbox.SetFocusMode(Control.FocusModeEnum.None);
            }
        }
    }

    private static Player? NodeToPlayer(Node? node)
    {
        return node switch
        {
            NMultiplayerPlayerState playerState => playerState.Player,
            NRestSiteCharacter restSiteCharacter => restSiteCharacter.Player,
            _ => null
        };
    }

    private static bool ShouldCancelTargeting()
    {
        return (NOverlayStack.Instance?.ScreenCount ?? 0) > 0 ||
               (NCapstoneContainer.Instance?.InUse ?? false);
    }

    private static bool AllowHoveringNode(Player owner, Node node)
    {
        Player? target = NodeToPlayer(node);
        return target != null && !LocalContext.IsMe(target) && target != owner;
    }
}
