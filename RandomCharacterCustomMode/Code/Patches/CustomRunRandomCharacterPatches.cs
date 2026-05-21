using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Unlocks;

namespace RandomCharacterCustomMode.Patches;

internal static class CustomRunRandomCharacterButtons
{
    private const string CharacterSelectButtonScenePath = "res://scenes/screens/char_select/char_select_button.tscn";

    private static readonly AccessTools.FieldRef<NCustomRunScreen, Control> CharButtonContainerRef =
        AccessTools.FieldRefAccess<NCustomRunScreen, Control>("_charButtonContainer");

    private static readonly AccessTools.FieldRef<NCustomRunScreen, LineEdit> SeedInputRef =
        AccessTools.FieldRefAccess<NCustomRunScreen, LineEdit>("_seedInput");

    private static readonly AccessTools.FieldRef<NCustomRunScreen, NRemoteLobbyPlayerContainer> RemotePlayerContainerRef =
        AccessTools.FieldRefAccess<NCustomRunScreen, NRemoteLobbyPlayerContainer>("_remotePlayerContainer");

    private static readonly AccessTools.FieldRef<NCustomRunScreen, NCharacterSelectButton?> SelectedButtonRef =
        AccessTools.FieldRefAccess<NCustomRunScreen, NCharacterSelectButton?>("_selectedButton");

    public static void AddRandomButton(NCustomRunScreen screen)
    {
        Control container = CharButtonContainerRef(screen);
        if (GetRandomButton(container) != null)
        {
            return;
        }

        CharacterModel randomCharacter = ModelDb.Character<RandomCharacter>();
        NCharacterSelectButton button = PreloadManager.Cache.GetScene(CharacterSelectButtonScenePath)
            .Instantiate<NCharacterSelectButton>(PackedScene.GenEditState.Disabled);

        button.Name = randomCharacter.Id.Entry + "_button";
        button.Visible = false;
        container.AddChild(button);
        button.Init(randomCharacter, screen);

        UpdateRandomVisibility(screen);
    }

    public static void UpdateRandomVisibility(NCustomRunScreen screen)
    {
        Control container = CharButtonContainerRef(screen);
        NCharacterSelectButton? randomButton = GetRandomButton(container);
        if (randomButton == null || screen.Lobby == null)
        {
            ConfigureFocusNeighbors(screen);
            return;
        }

        randomButton.Visible = screen.Lobby.Players.Any(static player => HasUnlockedAllCharacters(player));
        ConfigureFocusNeighbors(screen);
    }

    public static bool HandlePlayerChanged(
        NCustomRunScreen screen,
        LobbyPlayer player,
        bool isRandomCharacterResolution)
    {
        if (!isRandomCharacterResolution)
        {
            return true;
        }

        RemotePlayerContainerRef(screen).OnPlayerChanged(player);
        RefreshButtonSelectionForPlayer(screen, player);
        return false;
    }

    public static bool SelectRandomCharacterWithoutSfx(
        NCustomRunScreen screen,
        NCharacterSelectButton charSelectButton,
        CharacterModel characterModel)
    {
        if (!charSelectButton.IsRandom)
        {
            return true;
        }

        if (screen.Lobby == null)
        {
            throw new InvalidOperationException("Cannot select character while loading!");
        }

        SelectedButtonRef(screen) = charSelectButton;
        foreach (NCharacterSelectButton button in CharButtonContainerRef(screen).GetChildren().OfType<NCharacterSelectButton>())
        {
            if (button != charSelectButton)
            {
                button.Deselect();
            }
        }

        screen.Lobby.SetLocalCharacter(characterModel);
        return false;
    }

    private static void ConfigureFocusNeighbors(NCustomRunScreen screen)
    {
        Control container = CharButtonContainerRef(screen);
        LineEdit seedInput = SeedInputRef(screen);
        List<NCharacterSelectButton> buttons = container.GetChildren()
            .OfType<NCharacterSelectButton>()
            .Where(static button => button.Visible)
            .ToList();

        for (int i = 0; i < buttons.Count; i++)
        {
            NCharacterSelectButton button = buttons[i];
            button.FocusNeighborLeft = i > 0 ? buttons[i - 1].GetPath() : button.GetPath();
            button.FocusNeighborRight = i < buttons.Count - 1 ? buttons[i + 1].GetPath() : button.GetPath();
            button.FocusNeighborTop = seedInput.GetPath();
            button.FocusNeighborBottom = button.GetPath();
        }
    }

    private static void RefreshButtonSelectionForPlayer(NCustomRunScreen screen, LobbyPlayer player)
    {
        if (player.id == screen.Lobby.LocalPlayer.id)
        {
            return;
        }

        foreach (NCharacterSelectButton button in CharButtonContainerRef(screen).GetChildren().OfType<NCharacterSelectButton>())
        {
            if (button.RemoteSelectedPlayers.Contains(player.id) && player.character != button.Character)
            {
                button.OnRemotePlayerDeselected(player.id);
            }
            else if (player.character == button.Character)
            {
                button.OnRemotePlayerSelected(player.id);
            }
        }
    }

    private static NCharacterSelectButton? GetRandomButton(Control container)
    {
        foreach (NCharacterSelectButton button in container.GetChildren().OfType<NCharacterSelectButton>())
        {
            if (button.Character is RandomCharacter)
            {
                return button;
            }
        }

        return null;
    }

    private static bool HasUnlockedAllCharacters(LobbyPlayer player)
    {
        UnlockState unlockState = UnlockState.FromSerializable(player.unlockState);
        foreach (CharacterModel character in ModelDb.AllCharacters)
        {
            if (!unlockState.Characters.Contains(character))
            {
                return false;
            }
        }

        return true;
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), "InitCharacterButtons")]
public static class NCustomRunScreenInitCharacterButtonsPatch
{
    public static void Postfix(NCustomRunScreen __instance)
    {
        CustomRunRandomCharacterButtons.AddRandomButton(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.PlayerConnected))]
public static class NCustomRunScreenPlayerConnectedPatch
{
    public static void Postfix(NCustomRunScreen __instance)
    {
        CustomRunRandomCharacterButtons.UpdateRandomVisibility(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.RemotePlayerDisconnected))]
public static class NCustomRunScreenRemotePlayerDisconnectedPatch
{
    public static void Postfix(NCustomRunScreen __instance)
    {
        CustomRunRandomCharacterButtons.UpdateRandomVisibility(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.PlayerChanged))]
public static class NCustomRunScreenPlayerChangedPatch
{
    public static bool Prefix(
        NCustomRunScreen __instance,
        LobbyPlayer player,
        bool isRandomCharacterResolution)
    {
        return CustomRunRandomCharacterButtons.HandlePlayerChanged(__instance, player, isRandomCharacterResolution);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.SelectCharacter))]
public static class NCustomRunScreenSelectCharacterPatch
{
    public static bool Prefix(
        NCustomRunScreen __instance,
        NCharacterSelectButton charSelectButton,
        CharacterModel characterModel)
    {
        return CustomRunRandomCharacterButtons.SelectRandomCharacterWithoutSfx(
            __instance,
            charSelectButton,
            characterModel);
    }
}
