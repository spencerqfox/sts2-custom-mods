using System.Collections.Generic;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using TheFlagellant.Characters;

namespace TheFlagellant.Patches;

[HarmonyPatch]
internal static class FlagellantAssetFallbackPatches
{
    private const string FlagellantVisualsImagePath = "res://images/characters/flagellant_character_art.png";
    private static readonly string IroncladIconPath = SceneHelper.GetScenePath("ui/character_icons/ironclad_icon");
    private static readonly string IroncladEnergyCounterPath = SceneHelper.GetScenePath("combat/energy_counters/ironclad_energy_counter");
    private static readonly string IroncladMerchantPath = SceneHelper.GetScenePath("merchant/characters/ironclad_merchant");
    private static readonly string IroncladRestSitePath = SceneHelper.GetScenePath("rest_site/characters/ironclad_rest_site");
    private static readonly string IroncladCharSelectBgPath = SceneHelper.GetScenePath("screens/char_select/char_select_bg_ironclad");
    private static readonly string IroncladTrailPath = SceneHelper.GetScenePath("vfx/card_trail_ironclad");
    private const string IroncladTransitionPath = "res://materials/transitions/ironclad_transition_mat.tres";

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.AssetPaths), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool AssetPathsPrefix(CharacterModel __instance, ref IEnumerable<string> __result)
    {
        if (!IsFlagellant(__instance))
        {
            return true;
        }

        __result = new[]
        {
            FlagellantVisualsImagePath,
            ImageHelper.GetImagePath("characters/flagellantenergy.png"),
            ImageHelper.GetImagePath("ui/top_panel/character_icon_ironclad.png"),
            IroncladIconPath,
            IroncladEnergyCounterPath,
            IroncladRestSitePath,
            IroncladMerchantPath,
            IroncladTransitionPath,
            ImageHelper.GetImagePath("packed/map/icons/map_marker_ironclad.png"),
            IroncladTrailPath
        };
        return false;
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.AssetPathsCharacterSelect), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool AssetPathsCharacterSelectPrefix(CharacterModel __instance, ref IEnumerable<string> __result)
    {
        if (!IsFlagellant(__instance))
        {
            return true;
        }

        __result = new[]
        {
            IroncladCharSelectBgPath,
            ImageHelper.GetImagePath("characters/flagellantcharactershot.png"),
            ImageHelper.GetImagePath("characters/flagellantcharacterselect.png"),
            ImageHelper.GetImagePath("ui/top_panel/character_icon_ironclad.png"),
            IroncladTransitionPath
        };
        return false;
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CreateVisuals))]
    [HarmonyPrefix]
    private static bool CreateVisualsPrefix(CharacterModel __instance, ref NCreatureVisuals __result)
    {
        if (!IsFlagellant(__instance))
        {
            return true;
        }

        __result = CreateFlagellantVisuals();
        return false;
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.IconTexture), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool IconTexturePrefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (!IsFlagellant(__instance))
        {
            return true;
        }

        __result = PreloadManager.Cache.GetTexture2D(ImageHelper.GetImagePath("ui/top_panel/character_icon_ironclad.png"));
        return false;
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.IconOutlineTexture), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool IconOutlineTexturePrefix(CharacterModel __instance, ref Texture2D __result)
    {
        if (!IsFlagellant(__instance))
        {
            return true;
        }

        __result = PreloadManager.Cache.GetTexture2D(ImageHelper.GetImagePath("ui/top_panel/character_icon_ironclad_outline.png"));
        return false;
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.Icon), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool IconPrefix(CharacterModel __instance, ref Control __result)
    {
        if (!IsFlagellant(__instance))
        {
            return true;
        }

        __result = PreloadManager.Cache.GetScene(IroncladIconPath).Instantiate<Control>(PackedScene.GenEditState.Disabled);
        return false;
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.TrailPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool TrailPathPrefix(CharacterModel __instance, ref string __result)
    {
        return FallbackString(__instance, ref __result, IroncladTrailPath);
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.EnergyCounterPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool EnergyCounterPathPrefix(CharacterModel __instance, ref string __result)
    {
        return FallbackString(__instance, ref __result, IroncladEnergyCounterPath);
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.MerchantAnimPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool MerchantAnimPathPrefix(CharacterModel __instance, ref string __result)
    {
        return FallbackString(__instance, ref __result, IroncladMerchantPath);
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.RestSiteAnimPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool RestSiteAnimPathPrefix(CharacterModel __instance, ref string __result)
    {
        return FallbackString(__instance, ref __result, IroncladRestSitePath);
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CharacterSelectBg), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool CharacterSelectBgPrefix(CharacterModel __instance, ref string __result)
    {
        return FallbackString(__instance, ref __result, IroncladCharSelectBgPath);
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CharacterSelectTransitionPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool CharacterSelectTransitionPathPrefix(CharacterModel __instance, ref string __result)
    {
        return FallbackString(__instance, ref __result, IroncladTransitionPath);
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.MapMarker), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool MapMarkerPrefix(CharacterModel __instance, ref CompressedTexture2D __result)
    {
        if (!IsFlagellant(__instance))
        {
            return true;
        }

        __result = PreloadManager.Cache.GetCompressedTexture2D(ImageHelper.GetImagePath("packed/map/icons/map_marker_ironclad.png"));
        return false;
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ArmPointingTexture), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool ArmPointingTexturePrefix(CharacterModel __instance, ref Texture2D __result)
    {
        return FallbackTexture(__instance, ref __result, "ui/hands/multiplayer_hand_ironclad_point.png");
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ArmRockTexture), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool ArmRockTexturePrefix(CharacterModel __instance, ref Texture2D __result)
    {
        return FallbackTexture(__instance, ref __result, "ui/hands/multiplayer_hand_ironclad_rock.png");
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ArmPaperTexture), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool ArmPaperTexturePrefix(CharacterModel __instance, ref Texture2D __result)
    {
        return FallbackTexture(__instance, ref __result, "ui/hands/multiplayer_hand_ironclad_paper.png");
    }

    [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.ArmScissorsTexture), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool ArmScissorsTexturePrefix(CharacterModel __instance, ref Texture2D __result)
    {
        return FallbackTexture(__instance, ref __result, "ui/hands/multiplayer_hand_ironclad_scissors.png");
    }

    private static bool FallbackString(CharacterModel character, ref string result, string value)
    {
        if (!IsFlagellant(character))
        {
            return true;
        }

        result = value;
        return false;
    }

    private static NCreatureVisuals CreateFlagellantVisuals()
    {
        NCreatureVisuals root = new()
        {
            Name = "TheFlagellant"
        };

        AddOwnedChild(root, new Sprite2D
        {
            Name = "Visuals",
            UniqueNameInOwner = true,
            Position = new Vector2(0f, -137.5f),
            Scale = new Vector2(0.275f, 0.275f),
            Texture = PreloadManager.Cache.GetTexture2D(FlagellantVisualsImagePath)
        });

        AddOwnedChild(root, new Control
        {
            Name = "Bounds",
            UniqueNameInOwner = true,
            Position = new Vector2(-115f, -275f),
            Size = new Vector2(230f, 275f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        });

        AddOwnedChild(root, CreateMarker("CenterPos", new Vector2(0f, -137.5f)));
        AddOwnedChild(root, CreateMarker("IntentPos", new Vector2(0f, -305f)));
        AddOwnedChild(root, CreateMarker("OrbPos", new Vector2(-47.5f, -137.5f)));
        AddOwnedChild(root, CreateMarker("TalkPos", new Vector2(40f, -252.5f)));

        return root;
    }

    private static Marker2D CreateMarker(string name, Vector2 position)
    {
        return new Marker2D
        {
            Name = name,
            UniqueNameInOwner = true,
            Position = position
        };
    }

    private static void AddOwnedChild(Node owner, Node child)
    {
        owner.AddChild(child);
        child.Owner = owner;
    }

    private static bool FallbackTexture(CharacterModel character, ref Texture2D result, string imagePath)
    {
        if (!IsFlagellant(character))
        {
            return true;
        }

        result = PreloadManager.Cache.GetTexture2D(ImageHelper.GetImagePath(imagePath));
        return false;
    }

    private static bool IsFlagellant(CharacterModel character)
    {
        return character is FlagellantCharacter;
    }
}
