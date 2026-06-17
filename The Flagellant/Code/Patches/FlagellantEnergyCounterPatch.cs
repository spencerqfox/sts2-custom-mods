using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using TheFlagellant.Characters;

namespace TheFlagellant.Patches;

// The energy counter is a scene of layered, spinning orb sprites (we fall back to the Ironclad's). The
// Flagellant ships a single flat energy icon instead, so after the counter is ready we hide the layered
// orb art and drop our flat texture into the "%Layers" container, keeping the energy number on top.
[HarmonyPatch]
internal static class FlagellantEnergyCounterPatch
{
    private const string EnergyImageInnerPath = "characters/flagellantenergy.png";
    private const string LayersNodeName = "%Layers";

    [HarmonyPatch(typeof(NEnergyCounter), "_Ready")]
    [HarmonyPostfix]
    private static void ReadyPostfix(NEnergyCounter __instance)
    {
        Player? player = Traverse.Create(__instance).Field("_player").GetValue<Player>();
        if (player?.Character is not FlagellantCharacter)
        {
            return;
        }

        Control? layers = __instance.GetNodeOrNull<Control>(LayersNodeName);
        if (layers == null)
        {
            return;
        }

        // Hide the Ironclad orb layers (Layer1, RotationLayers, Layer4, Layer5) but leave them in the
        // tree: RefreshLabel iterates these children to tint/dim the orb when energy reaches 0.
        foreach (Node child in layers.GetChildren())
        {
            if (child is CanvasItem canvasItem)
            {
                canvasItem.Visible = false;
            }
        }

        TextureRect orb = new()
        {
            Name = "FlagellantOrb",
            Texture = PreloadManager.Cache.GetTexture2D(ImageHelper.GetImagePath(EnergyImageInnerPath)),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        orb.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layers.AddChild(orb);
    }
}
