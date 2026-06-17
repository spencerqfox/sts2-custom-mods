using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using TheFlagellant.Characters;

namespace TheFlagellant.Patches;

// The character-select screen instantiates each character's CharacterSelectBg scene (we fall back to
// the Ironclad's animated Spine background) and parents it under the "AnimatedBg" container. There is
// no virtual texture hook for it, so after the screen builds the fallback we swap in a full-screen
// TextureRect showing the Flagellant's painted character-select art instead.
[HarmonyPatch]
internal static class FlagellantCharacterSelectBgPatch
{
    private const string BgImageInnerPath = "characters/flagellantcharacterselect.png";
    private const string ContainerNodeName = "AnimatedBg";
    private const string CustomBgName = "TheFlagellantCustomBg";

    // Fraction of the screen the art fills, centered (1.0 = full bleed). Lower = smaller art with a
    // wider margin all around. Tune this if the character-select art reads too big or too small.
    private const float BgFillFraction = 0.9f;

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
    [HarmonyPostfix]
    private static void SelectCharacterPostfix(
        NCharacterSelectButton charSelectButton,
        CharacterModel characterModel,
        NCharacterSelectScreen __instance)
    {
        if (characterModel is not FlagellantCharacter || charSelectButton.IsLocked)
        {
            return;
        }

        Control? container = __instance.GetNodeOrNull<Control>(ContainerNodeName);
        if (container == null)
        {
            return;
        }

        // The screen has just freed the previous bg and added the Ironclad fallback. Free that fallback
        // (running on the main thread, RemoveChildSafely is synchronous) and replace it with our art.
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChildSafely(child);
            child.QueueFreeSafely();
        }

        Control bg = new()
        {
            Name = CustomBgName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both
        };
        // Center the art and inset it symmetrically so it fills BgFillFraction of the screen.
        const float margin = (1f - BgFillFraction) / 2f;
        bg.AnchorLeft = margin;
        bg.AnchorTop = margin;
        bg.AnchorRight = 1f - margin;
        bg.AnchorBottom = 1f - margin;
        bg.OffsetLeft = 0f;
        bg.OffsetTop = 0f;
        bg.OffsetRight = 0f;
        bg.OffsetBottom = 0f;

        TextureRect art = new()
        {
            Name = "Art",
            Texture = PreloadManager.Cache.GetTexture2D(ImageHelper.GetImagePath(BgImageInnerPath)),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        bg.AddChild(art);
        container.AddChildSafely(bg);
    }
}
