using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace FogOfWar;

internal static class FogVisibility
{
    public static readonly AccessTools.FieldRef<NMapPoint, IRunState> RunStateRef =
        AccessTools.FieldRefAccess<NMapPoint, IRunState>("_runState");

    public static readonly AccessTools.FieldRef<NMapScreen, Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>> PathsRef =
        AccessTools.FieldRefAccess<NMapScreen, Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>>("_paths");

    public static readonly AccessTools.FieldRef<NMapScreen, Dictionary<MapCoord, NMapPoint>> MapPointDictRef =
        AccessTools.FieldRefAccess<NMapScreen, Dictionary<MapCoord, NMapPoint>>("_mapPointDictionary");

    public static bool ShouldShow(NMapPoint node, IRunState? runState)
    {
        MapPoint point = node.Point;

        if (point.PointType == MapPointType.Boss)
        {
            return true;
        }

        if (node.State == MapPointState.Traveled)
        {
            return true;
        }

        if (runState == null)
        {
            return true;
        }

        MapPoint? current = runState.CurrentMapPoint;
        if (current == null)
        {
            return point.coord.row == 0;
        }

        if (point.coord == current.coord)
        {
            return true;
        }

        if (current.Children.Contains(point))
        {
            return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(NMapPoint), "RefreshState")]
public static class NMapPoint_RefreshState_Patch
{
    public static void Postfix(NMapPoint __instance)
    {
        __instance.Visible = FogVisibility.ShouldShow(__instance, FogVisibility.RunStateRef(__instance));
    }
}

[HarmonyPatch(typeof(NNormalMapPoint), "_Ready")]
public static class NNormalMapPoint_Ready_Patch
{
    public static void Postfix(NNormalMapPoint __instance)
    {
        __instance.Visible = FogVisibility.ShouldShow(__instance, FogVisibility.RunStateRef(__instance));
    }
}

[HarmonyPatch(typeof(NMapScreen), "RecalculateTravelability")]
public static class NMapScreen_RecalculateTravelability_Patch
{
    public static void Postfix(NMapScreen __instance)
    {
        var paths = FogVisibility.PathsRef(__instance);
        var dict = FogVisibility.MapPointDictRef(__instance);
        if (paths == null || dict == null)
        {
            return;
        }

        foreach (var kvp in paths)
        {
            var (fromCoord, toCoord) = kvp.Key;
            if (!dict.TryGetValue(fromCoord, out NMapPoint? fromNode) ||
                !dict.TryGetValue(toCoord, out NMapPoint? toNode) ||
                fromNode == null || toNode == null)
            {
                continue;
            }

            IRunState? runState = FogVisibility.RunStateRef(fromNode);
            bool visible = FogVisibility.ShouldShow(fromNode, runState)
                           && FogVisibility.ShouldShow(toNode, runState);

            foreach (TextureRect rect in kvp.Value)
            {
                rect.Visible = visible;
            }
        }
    }
}
