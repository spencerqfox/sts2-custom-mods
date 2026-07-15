using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.addons.mega_text;
using JoinInProgress.Join;
using JoinInProgress.Networking;

namespace JoinInProgress.Resync;

internal sealed partial class CatchUpScreen : Control
{
    private static CatchUpScreen? _active;

    private readonly HashSet<string> _purchasedShopItems = new();
    private readonly HashSet<MapPointHistoryEntry> _handledEventOutcomes = new();
    private RunState _state = null!;
    private Player _player = null!;
    private CatchUpRewardPlan _plan = null!;
    private List<CatchUpStep> _steps = null!;
    private int _stepIndex;
    private bool _busy;
    private bool _waitingForSnapshot;
    private Label _progress = null!;
    private Label _title = null!;
    private Label _summary = null!;
    private Label _status = null!;
    private VBoxContainer _choices = null!;

    public static void Show(RunState state, Player player, CatchUpRewardPlan plan)
    {
        _active?.QueueFreeSafely();
        CatchUpScreen screen = new()
        {
            Name = "JoinInProgressCatchUp",
            _state = state,
            _player = player,
            _plan = plan,
            _steps = BuildSteps(state, plan)
        };
        _active = screen;
        (NRun.Instance ?? throw new InvalidOperationException("The run scene is unavailable."))
            .AddChildSafely(screen);
    }

    public static void NotifySynchronized(ulong playerId)
    {
        if (_active == null || _active._player.NetId != playerId)
        {
            return;
        }

        _active._status.Text = "Synchronized. The next room is ready.";
        CatchUpScreen screen = _active;
        _active = null;
        screen.QueueFreeSafely();
    }

    public static void NotifyAborted(ulong playerId)
    {
        if (_active == null || _active._player.NetId != playerId)
        {
            return;
        }

        CatchUpScreen screen = _active;
        _active = null;
        screen.QueueFreeSafely();
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        ZIndex = 1000;

        ColorRect backstop = new()
        {
            Color = new Color(0.012f, 0.017f, 0.026f, 0.965f),
            MouseFilter = MouseFilterEnum.Stop
        };
        backstop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backstop);

        PanelContainer panel = new();
        panel.AnchorLeft = 0.5f;
        panel.AnchorTop = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -520f;
        panel.OffsetTop = -390f;
        panel.OffsetRight = 520f;
        panel.OffsetBottom = 390f;
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        AddChild(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 42);
        margin.AddThemeConstantOverride("margin_top", 32);
        margin.AddThemeConstantOverride("margin_right", 42);
        margin.AddThemeConstantOverride("margin_bottom", 30);
        panel.AddChild(margin);

        VBoxContainer layout = new();
        layout.AddThemeConstantOverride("separation", 12);
        margin.AddChild(layout);

        _progress = NewLabel(string.Empty, 17, StsColors.lightGray);
        _title = NewLabel(string.Empty, 34, StsColors.gold);
        _summary = NewLabel(string.Empty, 19, StsColors.cream);
        _status = NewLabel(string.Empty, 18, StsColors.aqua);
        layout.AddChild(_progress);
        layout.AddChild(_title);
        layout.AddChild(_summary);
        layout.AddChild(_status);
        layout.AddChild(new HSeparator());

        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        layout.AddChild(scroll);
        _choices = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _choices.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_choices);

        ShowCurrentStep();
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }
    }

    private static List<CatchUpStep> BuildSteps(RunState state, CatchUpRewardPlan plan)
    {
        List<CatchUpStep> steps = new();
        int globalFloor = 0;
        for (int actIndex = 0; actIndex < state.MapPointHistory.Count; actIndex++)
        {
            IReadOnlyList<MapPointHistoryEntry> act = state.MapPointHistory[actIndex];
            for (int floorIndex = 0; floorIndex < act.Count; floorIndex++)
            {
                globalFloor++;
                MapPointHistoryEntry floor = act[floorIndex];
                CatchUpFloorRewardPlan floorPlan = plan.Floors.First(candidate =>
                    candidate.ActIndex == actIndex && candidate.FloorIndex == floorIndex);
                List<MapPointRoomHistoryEntry> rooms = floor.Rooms
                    .Where(room => room.RoomType is not RoomType.Map and not RoomType.Unassigned)
                    .ToList();
                if (rooms.Count == 0)
                {
                    steps.Add(new CatchUpStep(actIndex, globalFloor, floor, null, null, IsLastRoom: true));
                    continue;
                }

                for (int roomIndex = 0; roomIndex < rooms.Count; roomIndex++)
                {
                    steps.Add(new CatchUpStep(
                        actIndex,
                        globalFloor,
                        floor,
                        rooms[roomIndex],
                        floorPlan.Rooms[roomIndex],
                        roomIndex == rooms.Count - 1));
                }
            }
        }
        return steps;
    }

    private void ShowCurrentStep()
    {
        ClearChoices();
        _status.Text = string.Empty;
        if (_stepIndex >= _steps.Count)
        {
            FinishCatchUp();
            return;
        }

        CatchUpStep step = _steps[_stepIndex];
        int mirroredDamage = GetMirroredDamage(step.Floor);
        _progress.Text = $"ACT {step.ActIndex + 1}  ·  FLOOR {step.GlobalFloor}  ·  {_stepIndex + 1}/{_steps.Count}";
        _title.Text = GetRoomTitle(step);
        _summary.Text =
            $"HP {_player.Creature.CurrentHp}/{_player.Creature.MaxHp}    Gold {_player.Gold}    " +
            $"Recorded floor damage {mirroredDamage}";

        if (step.Floor.MapPointType == MapPointType.Ancient)
        {
            ShowAncient(step);
            return;
        }

        switch (step.Room?.RoomType)
        {
            case RoomType.Monster:
            case RoomType.Elite:
            case RoomType.Boss:
                ShowCombatReward(step);
                break;
            case RoomType.RestSite:
                ShowRestSite(step);
                break;
            case RoomType.Shop:
                ShowShop(step);
                break;
            case RoomType.Treasure:
                ShowTreasure(step);
                break;
            case RoomType.Event:
                ShowEvent(step);
                break;
            default:
                AddActionButton("Continue", () => CompleteStep(step));
                break;
        }
    }

    private void ShowCombatReward(CatchUpStep step)
    {
        ShowCombatCardGroup(step, 0);
    }

    private void ShowCombatCardGroup(CatchUpStep step, int groupIndex)
    {
        IReadOnlyList<CatchUpCardRewardGroup> groups = step.Rewards?.CardRewardGroups ?? [];
        if (groupIndex >= groups.Count)
        {
            ShowCombatRelic(step, 0);
            return;
        }

        ClearChoices();
        List<SerializableCard> cards = groups[groupIndex].Cards;
        AddInfo($"Choose a personal card reward ({groupIndex + 1}/{groups.Count}), or skip it.");
        foreach (SerializableCard card in cards)
        {
            AddActionButton(GetCardName(card), () =>
            {
                AddCard(card, step.Floor);
                RecordCardChoices(step.Floor, cards.Select(value =>
                    new CardChoiceHistoryEntry { Card = value }).ToList(), card.Id);
                ShowCombatCardGroup(step, groupIndex + 1);
                return Task.CompletedTask;
            });
        }
        AddActionButton("Skip card reward", () =>
        {
            RecordCardChoices(step.Floor, cards.Select(value =>
                new CardChoiceHistoryEntry { Card = value }).ToList(), null);
            ShowCombatCardGroup(step, groupIndex + 1);
            return Task.CompletedTask;
        });
    }

    private void ShowCombatRelic(CatchUpStep step, int rewardIndex)
    {
        IReadOnlyList<ModelId> relics = step.Rewards?.RelicRewards ?? [];
        if (rewardIndex >= relics.Count)
        {
            ShowCombatPotion(step, 0);
            return;
        }

        ClearChoices();
        ModelId relicId = relics[rewardIndex];
        AddInfo($"Personal relic reward {rewardIndex + 1}/{relics.Count}.");
        if (CanReplayRelic(ModelDb.GetById<RelicModel>(relicId)))
        {
            AddActionButton($"Take {GetModelName<RelicModel>(relicId)}", async () =>
            {
                if (await AddRelic(relicId, step.Floor))
                {
                    ShowCombatRelic(step, rewardIndex + 1);
                }
            });
        }
        else
        {
            AddInfo("This relic has pickup logic that cannot be replayed safely.");
        }
        AddActionButton("Skip relic reward", () =>
        {
            GetLocalEntry(step.Floor).RelicChoices.Add(new ModelChoiceHistoryEntry(relicId, wasPicked: false));
            ShowCombatRelic(step, rewardIndex + 1);
            return Task.CompletedTask;
        });
    }

    private void ShowCombatPotion(CatchUpStep step, int rewardIndex)
    {
        IReadOnlyList<ModelId> potions = step.Rewards?.PotionRewards ?? [];
        if (rewardIndex >= potions.Count)
        {
            _ = CompleteStep(step);
            return;
        }

        ClearChoices();
        ModelId potionId = potions[rewardIndex];
        AddInfo($"Personal potion reward {rewardIndex + 1}/{potions.Count}.");
        if (!_player.HasOpenPotionSlots)
        {
            foreach (PotionModel existing in _player.Potions.ToList())
            {
                AddActionButton($"Discard {existing.Title.GetFormattedText()} and take {GetModelName<PotionModel>(potionId)}", () =>
                {
                    _player.DiscardPotionInternal(existing);
                    GetLocalEntry(step.Floor).PotionDiscarded.Add(existing.Id);
                    _player.AddPotionInternal(ModelDb.GetById<PotionModel>(potionId).ToMutable());
                    GetLocalEntry(step.Floor).PotionChoices.Add(new ModelChoiceHistoryEntry(potionId, wasPicked: true));
                    ShowCombatPotion(step, rewardIndex + 1);
                    return Task.CompletedTask;
                });
            }
        }
        AddActionButton($"Take {GetModelName<PotionModel>(potionId)}", () =>
        {
            var result = _player.AddPotionInternal(ModelDb.GetById<PotionModel>(potionId).ToMutable());
            if (!result.success)
            {
                _status.Text = "No open potion slot. Skip this reward or discard a potion before retrying.";
                return Task.CompletedTask;
            }
            GetLocalEntry(step.Floor).PotionChoices.Add(new ModelChoiceHistoryEntry(potionId, wasPicked: true));
            ShowCombatPotion(step, rewardIndex + 1);
            return Task.CompletedTask;
        });
        AddActionButton("Skip potion reward", () =>
        {
            GetLocalEntry(step.Floor).PotionChoices.Add(new ModelChoiceHistoryEntry(potionId, wasPicked: false));
            ShowCombatPotion(step, rewardIndex + 1);
            return Task.CompletedTask;
        });
    }

    private void ShowRestSite(CatchUpStep step)
    {
        decimal heal = MendRestSiteOption.GetHealAmount(_player);
        AddInfo("Choose a rest-site action for this completed floor.");
        AddActionButton($"Rest — heal {(int)heal} HP", () =>
        {
            int oldHp = _player.Creature.CurrentHp;
            _player.Creature.HealInternal(heal);
            PlayerMapPointHistoryEntry entry = GetLocalEntry(step.Floor);
            entry.HpHealed += _player.Creature.CurrentHp - oldHp;
            entry.RestSiteChoices.Add("MEND");
            return CompleteStep(step);
        });

        Button smith = AddActionButton("Smith — upgrade a card", () =>
        {
            ShowDeckForUpgrade(step);
            return Task.CompletedTask;
        });
        smith.Disabled = !_player.Deck.Cards.Any(card => card.IsUpgradable);
    }

    private void ShowDeckForUpgrade(CatchUpStep step)
    {
        ClearChoices();
        AddInfo("Choose a card to upgrade.");
        foreach (CardModel card in _player.Deck.Cards.Where(card => card.IsUpgradable).ToList())
        {
            AddActionButton(card.Title, () =>
            {
                card.UpgradeInternal();
                card.FinalizeUpgradeInternal();
                PlayerMapPointHistoryEntry entry = GetLocalEntry(step.Floor);
                entry.UpgradedCards.Add(card.Id);
                entry.RestSiteChoices.Add("SMITH");
                return CompleteStep(step);
            });
        }
        AddActionButton("Back", () =>
        {
            ShowCurrentStep();
            return Task.CompletedTask;
        });
    }

    private void ShowShop(CatchUpStep step)
    {
        CatchUpShopRewardPlan shop = step.Rewards?.Shop ??
            throw new InvalidOperationException("The personal shop plan is missing.");
        AddInfo($"Your personal shop stock · Your gold: {_player.Gold}. Buy any items, then leave.");

        int index = 0;
        foreach (CatchUpShopCardOffer offer in shop.Cards)
        {
            string key = $"{_stepIndex}:card:{index++}";
            SerializableCard card = offer.Card;
            int cost = offer.Cost;
            AddShopButton(key, $"Card · {GetCardName(card)} — {cost} gold", cost, () =>
            {
                AddCard(card, step.Floor);
                GetLocalEntry(step.Floor).CardChoices.Add(new CardChoiceHistoryEntry
                {
                    Card = card,
                    wasPicked = true
                });
                return Task.FromResult(true);
            });
        }

        index = 0;
        foreach (CatchUpShopModelOffer offer in shop.Relics)
        {
            string key = $"{_stepIndex}:relic:{index++}";
            ModelId relicId = offer.ModelId;
            RelicModel relic = ModelDb.GetById<RelicModel>(relicId);
            if (!CanReplayRelic(relic))
            {
                AddInfo($"{relic.Title.GetFormattedText()} is unavailable during catch-up because it has custom pickup logic.");
                continue;
            }
            int cost = offer.Cost;
            AddShopButton(key, $"Relic · {GetModelName<RelicModel>(relicId)} — {cost} gold", cost,
                () => AddRelic(relicId, step.Floor));
        }

        index = 0;
        foreach (CatchUpShopModelOffer offer in shop.Potions)
        {
            string key = $"{_stepIndex}:potion:{index++}";
            ModelId potionId = offer.ModelId;
            PotionModel potion = ModelDb.GetById<PotionModel>(potionId);
            int cost = offer.Cost;
            AddShopButton(key, $"Potion · {potion.Title.GetFormattedText()} — {cost} gold", cost, () =>
            {
                var result = _player.AddPotionInternal(potion.ToMutable());
                if (!result.success)
                {
                    ShowShopPotionReplacement(step, key, cost, potionId);
                    return Task.FromResult(false);
                }
                GetLocalEntry(step.Floor).BoughtPotions.Add(potionId);
                return Task.FromResult(true);
            });
        }

        int removalCost = 75 + 25 * _player.ExtraFields.CardShopRemovalsUsed;
        string removalKey = $"{_stepIndex}:remove";
        AddShopButton(removalKey, $"Remove a card — {removalCost} gold", removalCost, () =>
        {
            ShowDeckForRemoval(step, removalKey, removalCost);
            return Task.FromResult(true);
        }, spendImmediately: false);
        AddActionButton("Leave shop", () => CompleteStep(step));
    }

    private void ShowShopPotionReplacement(CatchUpStep step, string key, int cost, ModelId potionId)
    {
        ClearChoices();
        AddInfo($"Choose a potion to discard before buying {GetModelName<PotionModel>(potionId)}.");
        foreach (PotionModel existing in _player.Potions.ToList())
        {
            AddActionButton($"Discard {existing.Title.GetFormattedText()}", () =>
            {
                if (_player.Gold < cost)
                {
                    throw new InvalidOperationException("Not enough gold.");
                }
                _player.DiscardPotionInternal(existing);
                _player.AddPotionInternal(ModelDb.GetById<PotionModel>(potionId).ToMutable());
                _player.Gold -= cost;
                _purchasedShopItems.Add(key);
                PlayerMapPointHistoryEntry entry = GetLocalEntry(step.Floor);
                entry.PotionDiscarded.Add(existing.Id);
                entry.BoughtPotions.Add(potionId);
                entry.GoldSpent += cost;
                ShowCurrentStep();
                return Task.CompletedTask;
            });
        }
        AddActionButton("Back to shop", () =>
        {
            ShowCurrentStep();
            return Task.CompletedTask;
        });
    }

    private void ShowDeckForRemoval(CatchUpStep step, string key, int cost)
    {
        ClearChoices();
        AddInfo("Choose a card to remove.");
        foreach (CardModel card in _player.Deck.Cards.ToList())
        {
            AddActionButton(card.Title, () =>
            {
                if (_player.Gold < cost)
                {
                    throw new InvalidOperationException("Not enough gold.");
                }
                _player.Gold -= cost;
                _player.ExtraFields.CardShopRemovalsUsed++;
                _purchasedShopItems.Add(key);
                _player.Deck.RemoveInternal(card);
                _state.RemoveCard(card);
                PlayerMapPointHistoryEntry entry = GetLocalEntry(step.Floor);
                entry.GoldSpent += cost;
                entry.CardsRemoved.Add(card.ToSerializable());
                ShowCurrentStep();
                return Task.CompletedTask;
            });
        }
        AddActionButton("Back to shop", () =>
        {
            ShowCurrentStep();
            return Task.CompletedTask;
        });
    }

    private void ShowTreasure(CatchUpStep step)
    {
        List<ModelId> relics = step.Rewards?.RelicRewards ?? [];
        List<ModelId> safeRelics = relics
            .Where(relicId => CanReplayRelic(ModelDb.GetById<RelicModel>(relicId)))
            .ToList();
        AddInfo(relics.Count == 0
            ? "The chest had no recorded relic."
            : safeRelics.Count == 0
                ? "The personal relic requires pickup logic that cannot be synchronized; skip this chest."
                : "Choose your personal chest relic.");
        foreach (ModelId relicId in safeRelics)
        {
            AddActionButton(GetModelName<RelicModel>(relicId), async () =>
            {
                if (await AddRelic(relicId, step.Floor))
                {
                    await CompleteStep(step);
                }
            });
        }
        AddActionButton(relics.Count == 0 ? "Continue" : "Skip chest", () =>
        {
            foreach (ModelId relicId in relics)
            {
                GetLocalEntry(step.Floor).RelicChoices.Add(new ModelChoiceHistoryEntry(relicId, wasPicked: false));
            }
            return CompleteStep(step);
        });
    }

    private void ShowEvent(CatchUpStep step)
    {
        PlayerMapPointHistoryEntry reference = GetReferenceEntry(step.Floor);
        string choices = reference.EventChoices.Count == 0
            ? "No event choice text was recorded."
            : string.Join("  →  ", reference.EventChoices.Select(choice => choice.Title.GetFormattedText()));
        AddInfo($"The run log records: {choices}");

        if (!_handledEventOutcomes.Add(step.Floor))
        {
            AddActionButton("Continue", () => CompleteStep(step));
            return;
        }

        AddActionButton("Follow the recorded outcome", async () =>
        {
            await ApplyRecordedEventOutcome(step, reference);
            await CompleteStep(step);
        });
        AddActionButton("Pass without copying the outcome", () => CompleteStep(step));
    }

    private void ShowAncient(CatchUpStep step)
    {
        List<CatchUpAncientRelicOffer> offers = step.Rewards?.AncientRelicChoices ?? [];
        if (!_handledEventOutcomes.Add(step.Floor))
        {
            AddActionButton("Continue", () => CompleteStep(step));
            return;
        }

        int oldHp = _player.Creature.CurrentHp;
        int healPercent = step.Rewards?.AncientHealPercent ?? 100;
        decimal healAmount = (_player.Creature.MaxHp - oldHp) * healPercent / 100m;
        _player.Creature.HealInternal(healAmount);
        GetLocalEntry(step.Floor).HpHealed += _player.Creature.CurrentHp - oldHp;

        AddInfo("Choose one of your personal replay-safe Ancient relics.");
        foreach (CatchUpAncientRelicOffer offer in offers)
        {
            ModelId relicId = offer.Relic.Id ??
                throw new InvalidOperationException("An Ancient relic offer has no model ID.");
            AddActionButton(GetModelName<RelicModel>(relicId), async () =>
            {
                if (!await AddRelic(
                        relicId,
                        step.Floor,
                        serializedRelic: offer.Relic))
                {
                    return;
                }
                RecordAncientChoices(step.Floor, offers, relicId);
                await CompleteStep(step);
            });
        }
        if (offers.Count == 0)
        {
            AddActionButton("Continue", () => CompleteStep(step));
        }
    }

    private void RecordAncientChoices(
        MapPointHistoryEntry floor,
        IReadOnlyList<CatchUpAncientRelicOffer> offers,
        ModelId selected)
    {
        PlayerMapPointHistoryEntry local = GetLocalEntry(floor);
        foreach (CatchUpAncientRelicOffer offer in offers)
        {
            ModelId relicId = offer.Relic.Id ??
                throw new InvalidOperationException("An Ancient relic offer has no model ID.");
            local.AncientChoices.Add(new AncientChoiceHistoryEntry(
                ModelDb.GetById<RelicModel>(relicId).Title,
                relicId == selected));
        }
    }

    private async Task ApplyRecordedEventOutcome(CatchUpStep step, PlayerMapPointHistoryEntry reference)
    {
        int recordedSpend = step.Floor.HasRoomOfType(RoomType.Shop) ? 0 : Math.Max(0, reference.GoldSpent);
        if (_player.Gold < recordedSpend)
        {
            throw new InvalidOperationException("You do not have enough gold to follow the recorded event outcome.");
        }

        PlayerMapPointHistoryEntry local = GetLocalEntry(step.Floor);
        local.EventChoices.AddRange(reference.EventChoices);
        _player.Gold -= recordedSpend;
        local.GoldSpent += recordedSpend;

        int maxHpDelta = reference.MaxHpGained - reference.MaxHpLost;
        if (maxHpDelta != 0)
        {
            _player.Creature.SetMaxHpInternal(Math.Max(1, _player.Creature.MaxHp + maxHpDelta));
            local.MaxHpGained += Math.Max(0, maxHpDelta);
            local.MaxHpLost += Math.Max(0, -maxHpDelta);
        }
        if (reference.HpHealed > 0)
        {
            int oldHp = _player.Creature.CurrentHp;
            _player.Creature.HealInternal(reference.HpHealed);
            local.HpHealed += _player.Creature.CurrentHp - oldHp;
        }

        bool eventOnlyFloor = step.Floor.Rooms.All(room => room.RoomType == RoomType.Event);
        if (!eventOnlyFloor)
        {
            return;
        }

        foreach (SerializableCard card in reference.CardsGained)
        {
            AddCard(card, step.Floor);
        }
        foreach (SerializableCard removed in reference.CardsRemoved)
        {
            CardModel? card = _player.Deck.Cards.FirstOrDefault(candidate => candidate.Id == removed.Id);
            if (card == null)
            {
                continue;
            }
            local.CardsRemoved.Add(card.ToSerializable());
            _player.Deck.RemoveInternal(card);
            _state.RemoveCard(card);
        }
        foreach (ModelId upgradedId in reference.UpgradedCards)
        {
            CardModel? card = _player.Deck.Cards.FirstOrDefault(candidate =>
                candidate.Id == upgradedId && candidate.IsUpgradable);
            if (card == null)
            {
                continue;
            }
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
            local.UpgradedCards.Add(card.Id);
        }
        foreach (ModelChoiceHistoryEntry relic in reference.RelicChoices.Where(choice => choice.wasPicked))
        {
            if (CanReplayRelic(ModelDb.GetById<RelicModel>(relic.choice)))
            {
                await AddRelic(relic.choice, step.Floor);
            }
        }
        foreach (ModelChoiceHistoryEntry potion in reference.PotionChoices.Where(choice => choice.wasPicked))
        {
            var result = _player.AddPotionInternal(ModelDb.GetById<PotionModel>(potion.choice).ToMutable());
            if (result.success)
            {
                local.PotionChoices.Add(new ModelChoiceHistoryEntry(potion.choice, wasPicked: true));
            }
        }
    }

    private Button AddShopButton(
        string key,
        string label,
        int cost,
        Func<Task<bool>> purchase,
        bool spendImmediately = true)
    {
        Button button = AddActionButton(label, async () =>
        {
            if (_purchasedShopItems.Contains(key))
            {
                return;
            }
            if (_player.Gold < cost)
            {
                throw new InvalidOperationException("Not enough gold.");
            }
            bool purchased = await purchase();
            if (!purchased)
            {
                return;
            }
            if (spendImmediately)
            {
                _player.Gold -= cost;
                GetLocalEntry(_steps[_stepIndex].Floor).GoldSpent += cost;
                _purchasedShopItems.Add(key);
                ShowCurrentStep();
            }
        });
        button.Disabled = _purchasedShopItems.Contains(key) || _player.Gold < cost;
        return button;
    }

    private async Task<bool> AddRelic(
        ModelId relicId,
        MapPointHistoryEntry floor,
        SerializableRelic? serializedRelic = null)
    {
        RelicModel canonical = ModelDb.GetById<RelicModel>(relicId);
        if (!CanReplayRelic(canonical))
        {
            _status.Text = $"{canonical.Title.GetFormattedText()} has custom pickup logic and cannot be synchronized safely.";
            return false;
        }
        if (!canonical.IsStackable && _player.Relics.Any(relic => relic.Id == relicId))
        {
            _status.Text = $"{canonical.Title.GetFormattedText()} cannot be stacked.";
            return false;
        }

        RelicModel relic = serializedRelic == null
            ? canonical.ToMutable()
            : RelicModel.FromSerializable(serializedRelic);
        _player.AddRelicInternal(relic);
        try
        {
            await relic.AfterObtained();
        }
        catch
        {
            _player.RemoveRelicInternal(relic, silent: true);
            throw;
        }
        relic.FloorAddedToDeck = _state.MapPointHistory.SelectMany(act => act).ToList().IndexOf(floor) + 1;
        _player.RelicGrabBag.Remove(canonical);
        GetLocalEntry(floor).RelicChoices.Add(new ModelChoiceHistoryEntry(relicId, wasPicked: true));
        return true;
    }

    private void AddCard(SerializableCard serializedCard, MapPointHistoryEntry floor)
    {
        CardModel card = _state.LoadCard(serializedCard, _player);
        _player.Deck.AddInternal(card);
        GetLocalEntry(floor).CardsGained.Add(card.ToSerializable());
    }

    private void RecordCardChoices(
        MapPointHistoryEntry floor,
        IReadOnlyList<CardChoiceHistoryEntry> choices,
        ModelId? selected)
    {
        PlayerMapPointHistoryEntry local = GetLocalEntry(floor);
        foreach (CardChoiceHistoryEntry original in choices)
        {
            CardChoiceHistoryEntry copy = original;
            copy.wasPicked = selected != null && copy.Card.Id == selected;
            local.CardChoices.Add(copy);
        }
    }

    private Task CompleteStep(CatchUpStep step)
    {
        if (step.IsLastRoom)
        {
            ApplyFloorLedger(step);
        }
        _stepIndex++;
        ShowCurrentStep();
        return Task.CompletedTask;
    }

    private void ApplyFloorLedger(CatchUpStep step)
    {
        PlayerMapPointHistoryEntry reference = GetReferenceEntry(step.Floor);
        PlayerMapPointHistoryEntry local = GetLocalEntry(step.Floor);
        CatchUpFloorRewardPlan floorPlan = _plan.Floors.First(plan => plan.GlobalFloor == step.GlobalFloor);

        bool generatedCombatGold = floorPlan.Rooms.Any(room =>
            room.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss);
        int goldGain = generatedCombatGold
            ? floorPlan.Rooms.Sum(room => Math.Max(0, room.Gold))
            : Math.Max(0, reference.GoldGained);
        int goldLoss = Math.Max(0, reference.GoldLost + reference.GoldStolen);
        _player.Gold = Math.Max(0, _player.Gold + goldGain - goldLoss);
        local.GoldGained += goldGain;
        local.GoldLost += goldLoss;

        int mirroredDamage = GetMirroredDamage(step.Floor);
        int appliedDamage = Math.Min(mirroredDamage, Math.Max(0, _player.Creature.CurrentHp - 1));
        _player.Creature.SetCurrentHpInternal(_player.Creature.CurrentHp - appliedDamage);
        local.DamageTaken += mirroredDamage;
        local.CurrentHp = _player.Creature.CurrentHp;
        local.MaxHp = _player.Creature.MaxHp;
        local.CurrentGold = _player.Gold;

        if (mirroredDamage > appliedDamage)
        {
            _status.Text = $"Recorded damage was capped at 1 HP ({mirroredDamage} logged, {appliedDamage} applied).";
        }
    }

    private void FinishCatchUp()
    {
        if (_waitingForSnapshot)
        {
            return;
        }
        _waitingForSnapshot = true;
        _title.Text = "SYNCING WITH THE PARTY";
        _progress.Text = "CATCH-UP COMPLETE";
        _summary.Text = $"Final state · HP {_player.Creature.CurrentHp}/{_player.Creature.MaxHp} · Gold {_player.Gold}";
        _status.Text = "Waiting for the host to confirm your state...";
        AddInfo("Map travel will unlock for everyone after the host validates this snapshot.");
        LateJoinNetwork.SendCatchUpComplete(_player);
        TaskHelper.RunSafely(WaitForHostConfirmation());
    }

    private async Task WaitForHostConfirmation()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        if (!ReferenceEquals(_active, this) || !_waitingForSnapshot)
        {
            return;
        }

        _status.Text = "The host did not confirm catch-up. Disconnecting so the party can roll back safely.";
        RunManager.Instance.NetService.Disconnect(NetError.HandshakeTimeout);
    }

    private int GetMirroredDamage(MapPointHistoryEntry floor)
    {
        return GetReferenceEntry(floor).DamageTaken;
    }

    private PlayerMapPointHistoryEntry GetReferenceEntry(MapPointHistoryEntry floor)
    {
        List<PlayerMapPointHistoryEntry> originals = floor.PlayerStats
            .Where(entry => entry.PlayerId != _player.NetId)
            .ToList();
        return originals.FirstOrDefault(entry =>
                   _state.GetPlayer(entry.PlayerId)?.Character.Id == _player.Character.Id)
               ?? originals.First();
    }

    private static bool CanReplayRelic(RelicModel relic)
    {
        return !relic.HasUponPickupEffect &&
               relic.GetType().GetMethod(nameof(RelicModel.AfterObtained))?.DeclaringType == typeof(RelicModel);
    }

    private PlayerMapPointHistoryEntry GetLocalEntry(MapPointHistoryEntry floor)
    {
        return floor.GetEntry(_player.NetId);
    }

    private string GetRoomTitle(CatchUpStep step)
    {
        if (step.Room?.ModelId is ModelId modelId)
        {
            if (step.Room.RoomType == RoomType.Event)
            {
                return ModelDb.GetById<EventModel>(modelId).Title.GetFormattedText().ToUpperInvariant();
            }
            if (step.Room.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss)
            {
                return ModelDb.GetById<EncounterModel>(modelId).Title.GetFormattedText().ToUpperInvariant();
            }
        }
        return step.Room?.RoomType.ToString().ToUpperInvariant() ?? "FLOOR CHECKPOINT";
    }

    private static string GetCardName(SerializableCard card)
    {
        CardModel model = SaveUtil.CardOrDeprecated(
            card.Id ?? throw new InvalidOperationException("A recorded card has no model ID."));
        string suffix = card.CurrentUpgradeLevel > 0 ? $" +{card.CurrentUpgradeLevel}" : string.Empty;
        return model.Title + suffix;
    }

    private static string GetModelName<T>(ModelId id) where T : AbstractModel
    {
        AbstractModel model = ModelDb.GetById<T>(id);
        return model switch
        {
            RelicModel relic => relic.Title.GetFormattedText(),
            PotionModel potion => potion.Title.GetFormattedText(),
            CardModel card => card.Title,
            _ => id.Entry
        };
    }

    private Button AddActionButton(string text, Func<Task> action)
    {
        Button button = LateJoinCharacterPicker.NewButton(text);
        button.CustomMinimumSize = new Vector2(0f, 58f);
        button.Pressed += () => RunGuarded(action);
        _choices.AddChild(button);
        return button;
    }

    private void RunGuarded(Func<Task> action)
    {
        if (_busy || _waitingForSnapshot)
        {
            return;
        }
        TaskHelper.RunSafely(RunGuardedAsync(action));
    }

    private async Task RunGuardedAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Log.Error($"[JoinInProgress] Catch-up choice failed: {exception}");
            _status.Text = exception.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void AddInfo(string text)
    {
        Label label = NewLabel(text, 19, StsColors.cream);
        label.CustomMinimumSize = new Vector2(0f, 54f);
        _choices.AddChild(label);
    }

    private void ClearChoices()
    {
        if (_choices == null)
        {
            return;
        }
        foreach (Node child in _choices.GetChildren())
        {
            _choices.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Label NewLabel(string text, int fontSize, Color color)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontSizeOverride(ThemeConstants.Label.FontSize, fontSize);
        label.AddThemeColorOverride(ThemeConstants.Label.FontColor, color);
        label.ApplyLocaleFontSubstitution(FontType.Regular, ThemeConstants.Label.Font);
        return label;
    }

    private static StyleBoxFlat CreatePanelStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.052f, 0.071f, 0.995f),
            BorderColor = new Color(1f, 1f, 1f, 0.13f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 18,
            CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18,
            CornerRadiusBottomRight = 18
        };
    }

    private sealed record CatchUpStep(
        int ActIndex,
        int GlobalFloor,
        MapPointHistoryEntry Floor,
        MapPointRoomHistoryEntry? Room,
        CatchUpRoomRewardPlan? Rewards,
        bool IsLastRoom);
}
