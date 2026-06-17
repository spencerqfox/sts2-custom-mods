using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Random;

namespace CorrelatedRandomnessFix.Patches;

/// <summary>
/// Replaces each <see cref="Rng"/>'s backing <see cref="Random"/> with one seeded from a
/// nonlinear mix of the original seed. This keeps the game's System.Random counter semantics
/// while breaking the direct linear relationship between named RNG stream seeds.
/// </summary>
[HarmonyPatch]
public static class RngPatch
{
    private static readonly AccessTools.FieldRef<Rng, System.Random> RandomRef =
        AccessTools.FieldRefAccess<Rng, System.Random>("_random");

    private static MethodBase TargetMethod() =>
        AccessTools.Constructor(typeof(Rng), new[] { typeof(uint), typeof(int) });

    private static void Postfix(Rng __instance)
    {
        var random = new Random(MixSeed(__instance.Seed));
        for (int i = 0; i < __instance.Counter; i++)
        {
            random.Next();
        }

        RandomRef(__instance) = random;
    }

    private static int MixSeed(uint seed)
    {
        unchecked
        {
            uint mixed = seed + 0x9E3779B9u;
            mixed = (mixed ^ (mixed >> 16)) * 0x85EBCA6Bu;
            mixed = (mixed ^ (mixed >> 13)) * 0xC2B2AE35u;
            mixed ^= mixed >> 16;
            return (int)(mixed & 0x7FFFFFFFu);
        }
    }
}
