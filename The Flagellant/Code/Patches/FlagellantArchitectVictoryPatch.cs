using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheFlagellant.Characters;

namespace TheFlagellant.Patches;

// The Act-3 final-boss room is the TheArchitect event. Its post-victory dialogue is chosen from a
// CharacterDialogues dictionary that is hard-coded in C# with only the five base-game characters, so
// for the Flagellant LoadDialogue() finds nothing and the event's _dialogue field stays null. The game
// still shows a PROCEED button (GenerateInitialOptions handles the null case), but clicking it runs
// WinRun, which dereferences Dialogue.EndAttackers and throws a NullReferenceException BEFORE casting
// the player's "ready to advance" vote (ActChangeSynchronizer.SetLocalPlayerReady). In multiplayer the
// act change is gated on every player voting, so that lost vote hard-locks the whole lobby on the
// victory screen.
//
// The desired behaviour is "skip dialogue and just show a win", so when there is no dialogue we skip
// WinRun's attack-animation steps (which require a non-null Dialogue) and run only the bookkeeping that
// actually advances the run. Tightly gated to the Flagellant + the null-dialogue case, so base-game
// characters and any future real dialogue are unaffected.
[HarmonyPatch]
internal static class FlagellantArchitectVictoryPatch
{
    [HarmonyPatch(typeof(TheArchitect), "WinRun")]
    [HarmonyPrefix]
    private static bool WinRunPrefix(TheArchitect __instance, ref Task __result)
    {
        if (__instance.Owner is not { Character: FlagellantCharacter } owner)
        {
            return true;
        }

        // If a real dialogue somehow exists, let the original method run normally.
        AncientDialogue? dialogue = Traverse.Create(__instance).Field("_dialogue").GetValue<AncientDialogue>();
        if (dialogue != null)
        {
            return true;
        }

        if (LocalContext.IsMe(owner))
        {
            if (owner.RunState.Players.Count > 1)
            {
                NCombatRoom.Instance?.SetWaitingForOtherPlayersOverlayVisible(visible: true);
            }

            RunManager.Instance.ActChangeSynchronizer.SetLocalPlayerReady();
        }

        __result = Task.CompletedTask;
        return false;
    }
}
