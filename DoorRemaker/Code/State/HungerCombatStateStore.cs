using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;

namespace DoorRemaker.State;

public static class HungerCombatStateStore
{
    private static readonly ConditionalWeakTable<CombatState, HungerCombatState> States = new();

    public static HungerCombatState ForCombat(CombatState combatState)
    {
        return States.GetValue(combatState, static _ => new HungerCombatState());
    }
}
