using System;
using System.Collections.Generic;
using System.Linq;
using CombatEngine;
using CombatEngine.Engine;
using CombatEngine.DataClasses;
using CombatEngine.Enums;
using GameEngine;
using GameEngine.DataClasses;
using GameEngine.Engine;
using Godot;

public partial class Battle : Control
{
	[Export] private float _eventDelaySeconds = 0.5f;
	private double _eventDelayRemaining = 0.0;

	private Label         _roundLabel           = null!;
	private Label         _turnOrderLabel       = null!;
	private Label         _actionPaneTitle      = null!;
	private VBoxContainer _actionContainer      = null!;
	private Label         _selectedTargetsLabel = null!;
	private VBoxContainer _enemiesColumn        = null!;
	private VBoxContainer _alliesColumn         = null!;
	private ScrollContainer _logScroll          = null!;
	private VBoxContainer _logContainer         = null!;
	private Window _resultModal = null!;
	private Label  _resultLabel = null!;

	private readonly Dictionary<string, CombatantCard> _cardsById       = new();
	private readonly Dictionary<string, string>        _entityNamesById = new();
	private readonly List<ActionRow> _actionRows = new();

	private List<string> _pendingChosenTargets = new();
	private int  _pendingNumAttacks;
	private bool _pendingAllowMultipleAttackOnSameTarget;
	private bool _targetingActive;
	private HashSet<string> _targetableIds = new();

	private List<string> _currentTurnOrderIds   = new();
	private List<string> _currentTurnOrderNames = new();

	public override void _Ready()
	{
		_roundLabel           = GetNode<Label>("VBoxContainer/ContentArea/CenterPanel/RoundPane/RoundPaneVBox/RoundLabel");
		_turnOrderLabel       = GetNode<Label>("VBoxContainer/ContentArea/CenterPanel/RoundPane/RoundPaneVBox/TurnOrderLabel");
		_actionPaneTitle      = GetNode<Label>("VBoxContainer/ContentArea/CenterPanel/ActionPane/ActionPaneVBox/ActionPaneTitle");
		_actionContainer      = GetNode<VBoxContainer>("VBoxContainer/ContentArea/CenterPanel/ActionPane/ActionPaneVBox/ActionScroll/ActionContainer");
		_selectedTargetsLabel = GetNode<Label>("VBoxContainer/ContentArea/CenterPanel/ActionPane/ActionPaneVBox/SelectedTargetsLabel");
		_enemiesColumn        = GetNode<VBoxContainer>("VBoxContainer/ContentArea/EnemiesColumn");
		_alliesColumn         = GetNode<VBoxContainer>("VBoxContainer/ContentArea/AlliesColumn");
		_logScroll            = GetNode<ScrollContainer>("VBoxContainer/ContentArea/CenterPanel/LogPane/LogPaneVBox/LogScroll");
		_logContainer         = GetNode<VBoxContainer>("VBoxContainer/ContentArea/CenterPanel/LogPane/LogPaneVBox/LogScroll/LogContainer");
		_resultModal          = GetNode<Window>("ResultModal");
		_resultLabel          = GetNode<Label>("ResultModal/ModalVBox/ResultLabel");

		UiEventQueue.Clear();

		GetNode<Button>("VBoxContainer/HeaderRow/QuitBattleButton").Pressed += OnQuitBattlePressed;
		GetNode<Button>("ResultModal/ModalVBox/ReturnToMenuButton").Pressed += OnQuitBattlePressed;
		_resultModal.CloseRequested += () => _resultModal.Hide();

		var startData = GameEngineClass.Instance.InitSkirmishCombat();
		BuildCombatantCards(startData);
		AddLogEntry("Battle started!");

		CombatEventBus.RoundStarted           += OnRoundStarted;
		CombatEventBus.RoundEnded             += OnRoundEnded;
		CombatEventBus.TurnStarted            += OnTurnStarted;
		CombatEventBus.TurnEnded              += OnTurnEnded;
		CombatEventBus.WaitingForTurn          += OnWaitingForTurn;
		CombatEventBus.TargetSelectionRequested += OnTargetSelectionRequested;
		CombatEventBus.EntityDamaged           += OnEntityDamaged;
		CombatEventBus.EntityHealed            += OnEntityHealed;
		CombatEventBus.EntityTpChanged        += OnEntityTpChanged;
		CombatEventBus.EntityRevived          += OnEntityRevived;
		CombatEventBus.BuffDebuffApplied      += OnBuffDebuffApplied;
		CombatEventBus.BuffDebuffTicked       += OnBuffDebuffTicked;
		CombatEventBus.BuffDebuffExpired      += OnBuffDebuffExpired;
		CombatEventBus.RegenDrainApplied      += OnRegenDrainApplied;
		CombatEventBus.RegenDrainTicked       += OnRegenDrainTicked;
		CombatEventBus.RegenDrainExpired      += OnRegenDrainExpired;
		CombatEventBus.ActionResolved         += OnActionResolved;
		CombatEventBus.CombatOver             += OnCombatOver;

		GameEngineClass.Instance.BeginSkirmishCombat();
	}

	public override void _Process(double delta)
	{
		if (_eventDelayRemaining > 0)
		{
			_eventDelayRemaining -= delta;
			return;
		}

		if (UiEventQueue.TryDequeue(out var action))
		{
			action();
			_eventDelayRemaining = _eventDelaySeconds;
		}
	}

	private void BuildCombatantCards(CombatStartData startData)
	{
		var cardScene = GD.Load<PackedScene>("res://Scenes/CombatantCard.tscn");

		foreach (var seed in startData.Enemies)
		{
			var card = cardScene.Instantiate<CombatantCard>();
			_enemiesColumn.AddChild(card);
			card.Initialize(seed, showTp: false);
			RegisterCard(seed, card);
		}

		foreach (var seed in startData.Allies)
		{
			var card = cardScene.Instantiate<CombatantCard>();
			_alliesColumn.AddChild(card);
			card.Initialize(seed, showTp: true);
			RegisterCard(seed, card);
		}
	}

	private void RegisterCard(CombatantSeed seed, CombatantCard card)
	{
		_cardsById[seed.EntityId] = card;
		_entityNamesById[seed.EntityId] = seed.Name;
		card.Clicked += OnCardClicked;
	}

	// ---------------------------------------------------------------
	// Round / turn pane
	// ---------------------------------------------------------------

	private void OnRoundStarted(int round, IReadOnlyList<string> turnOrderIds, IReadOnlyList<string> turnOrderNames) =>
		UiEventQueue.Enqueue(() =>
		{
			_currentTurnOrderIds   = turnOrderIds.ToList();
			_currentTurnOrderNames = turnOrderNames.ToList();
			_roundLabel.Text = $"Round {round}";
			RenderTurnOrder(currentEntityId: null);
			AddLogEntry($"--- Round {round} started ---");
		});

	private void OnRoundEnded(int round) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"--- Round {round} ended ---"));

	private void OnTurnStarted(string entityId, string entityName) =>
		UiEventQueue.Enqueue(() =>
		{
			foreach (var card in _cardsById.Values)
				card.SetActiveTurn(false);
			if (_cardsById.TryGetValue(entityId, out var activeCard))
				activeCard.SetActiveTurn(true);

			RenderTurnOrder(entityId);
			AddLogEntry($"{entityName}'s turn.");
		});

	private void OnTurnEnded(string entityId, string entityName) =>
		UiEventQueue.Enqueue(() =>
		{
			if (_cardsById.TryGetValue(entityId, out var card))
				card.SetActiveTurn(false);
			AddLogEntry($"{entityName}'s turn ended.");
		});

	private void RenderTurnOrder(string? currentEntityId)
	{
		var parts = _currentTurnOrderIds.Zip(_currentTurnOrderNames,
			(id, name) => id == currentEntityId ? $"[{name}]" : name);
		_turnOrderLabel.Text = string.Join(", ", parts);
	}

	// ---------------------------------------------------------------
	// Action pane
	// ---------------------------------------------------------------

	private void OnWaitingForTurn(string entityId, string entityName, int currentTp, bool isAlly)
	{
		if (isAlly)
		{
			// Paced through UiEventQueue so the pane only lights up once the previous turn's log
			// lines and float animations have drained, instead of overlapping with them.
			UiEventQueue.Enqueue(() => PopulateActionPane(entityId, currentTp));
			return;
		}

		var cmd = GameEngineClass.Instance.ChooseAiCommand(entityId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void PopulateActionPane(string entityId, int currentTp)
	{
		ClearActionPane();

		var adventurer = GameEngineClass.Instance.AllAdventurers.Lookup(entityId);
		var actionRowScene = GD.Load<PackedScene>("res://Scenes/ActionRow.tscn");

		foreach (var techId in adventurer.TechsIds)
		{
			var tech = GameEngineClass.Instance.AllTechs.Lookup(techId);
			var row  = actionRowScene.Instantiate<ActionRow>();
			_actionContainer.AddChild(row);
			row.Initialize($"{tech.Name}  (TP: {tech.TpCost})", tech.Description,
				FormatBonuses(tech.Parameters, tech.Keywords, tech.NumAttacks), currentTp < tech.TpCost);

			var capturedActorId = entityId;
			var capturedTechId  = techId;
			row.Pressed += () => OnTechSelected(capturedActorId, capturedTechId);
			_actionRows.Add(row);
		}

		if (adventurer.CanUseFightAction)
		{
			var row = actionRowScene.Instantiate<ActionRow>();
			_actionContainer.AddChild(row);
			row.Initialize("Fight", "A basic physical attack.", "", disabled: false);

			var capturedActorId = entityId;
			row.Pressed += () => OnFightSelected(capturedActorId);
			_actionRows.Add(row);
		}

		foreach (var itemId in adventurer.ItemIds)
		{
			var item      = GameEngineClass.Instance.AllItems.Lookup(itemId);
			int remaining = GameEngineClass.Instance.GetRemainingItemUses(entityId, itemId);
			var row       = actionRowScene.Instantiate<ActionRow>();
			_actionContainer.AddChild(row);
			row.Initialize($"{item.Name}  ({remaining}/{item.MaxUses})", item.Description,
				FormatBonuses(item.Parameters, item.Keywords, item.NumAttacks), remaining <= 0);

			var capturedActorId = entityId;
			var capturedItemId  = itemId;
			row.Pressed += () => OnItemSelected(capturedActorId, capturedItemId);
			_actionRows.Add(row);
		}

		_actionPaneTitle.Text = adventurer.Name;
	}

	private void ClearActionPane()
	{
		foreach (Node child in _actionContainer.GetChildren())
			child.QueueFree();
		_actionRows.Clear();
		_actionPaneTitle.Text = "";
		_selectedTargetsLabel.Text = "";
	}

	// Bonuses summary shown under a tech/item's description: element, power, calc type when it's
	// not the plain formula, multi-hit count, active keywords, and one line per buffsDebuffs/
	// regensDrains entry the action carries.
	private static string FormatBonuses(CombatFunctionParameters parameters, List<string> keywords, int numAttacks)
	{
		var lines = new List<string>();

		if (parameters.Element.HasValue)
			lines.Add($"Element: {parameters.Element}");
		if (parameters.PowerFactor.HasValue)
			lines.Add($"Power: {parameters.PowerFactor}");
		if (parameters.CalcType.HasValue && parameters.CalcType != DamageOrHealCalcType.StandardFormula)
			lines.Add($"Calc: {parameters.CalcType}");
		if (numAttacks > 1)
			lines.Add($"{numAttacks} hits");
		if (keywords.Count > 0)
			lines.Add(string.Join(", ", keywords));

		foreach (var spec in parameters.BuffsDebuffs ?? [])
		{
			var duration = spec.UntilRemoved ? "until removed" : $"{spec.Rounds} round{(spec.Rounds == 1 ? "" : "s")}";
			lines.Add($"{spec.Stat} {(spec.Type == BuffDebuffType.Positive ? "up" : "down")} {duration} ({spec.Target})");
		}
		foreach (var spec in parameters.RegensDrains ?? [])
		{
			var duration = spec.UntilRemoved ? "until removed" : $"{spec.Rounds} round{(spec.Rounds == 1 ? "" : "s")}";
			lines.Add($"{spec.Stat} {(spec.Type == RegenDrainType.Positive ? "regen" : "drain")} {duration} ({spec.Target})");
		}

		return string.Join("\n", lines);
	}

	private void OnTechSelected(string actorId, string techId)
	{
		ClearActionPane();
		var cmd = GameEngineClass.Instance.MakeTechCommand(actorId, techId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void OnItemSelected(string actorId, string itemId)
	{
		ClearActionPane();
		var cmd = GameEngineClass.Instance.UseItem(actorId, itemId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void OnFightSelected(string actorId)
	{
		ClearActionPane();
		var cmd = GameEngineClass.Instance.MakeFightCommand(actorId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	// ---------------------------------------------------------------
	// Target selection - click the enemy/ally card directly
	// ---------------------------------------------------------------

	private void OnTargetSelectionRequested(string actorId, string actorName, TargetingType targetingType, IReadOnlyList<string> validTargetIds, IReadOnlyList<string> validTargetNames, int numAttacks, bool allowMultipleAttackOnSameTarget) =>
		UiEventQueue.Enqueue(() =>
		{
			_pendingChosenTargets = new List<string>();
			_pendingNumAttacks = numAttacks;
			_pendingAllowMultipleAttackOnSameTarget = allowMultipleAttackOnSameTarget;
			_targetingActive = true;
			_targetableIds = validTargetIds.ToHashSet();

			foreach (var row in _actionRows)
				row.SetInteractable(false);
			foreach (var id in _targetableIds)
				if (_cardsById.TryGetValue(id, out var card))
					card.SetTargetable(true);

			UpdateSelectedTargetsLabel();
		});

	private void OnCardClicked(string entityId)
	{
		if (!_targetingActive || !_targetableIds.Contains(entityId)) return;
		OnTargetChosen(entityId);
	}

	private void OnTargetChosen(string targetId)
	{
		_pendingChosenTargets.Add(targetId);

		if (!_pendingAllowMultipleAttackOnSameTarget)
		{
			_targetableIds.Remove(targetId);
			if (_cardsById.TryGetValue(targetId, out var pickedCard))
				pickedCard.SetTargetable(false);
		}

		if (_pendingChosenTargets.Count >= _pendingNumAttacks)
		{
			EndTargeting();
			CombatEngineClass.Instance.SubmitTargets(_pendingChosenTargets);
			return;
		}

		UpdateSelectedTargetsLabel();
	}

	private void EndTargeting()
	{
		_targetingActive = false;
		foreach (var id in _targetableIds)
			if (_cardsById.TryGetValue(id, out var card))
				card.SetTargetable(false);
		_targetableIds.Clear();
		ClearActionPane();
	}

	private void UpdateSelectedTargetsLabel()
	{
		var progress = _pendingNumAttacks > 1
			? $"Choose target ({_pendingChosenTargets.Count + 1}/{_pendingNumAttacks})"
			: "Choose target";

		if (_pendingChosenTargets.Count == 0)
		{
			_selectedTargetsLabel.Text = progress;
			return;
		}

		var selectedNames = _pendingChosenTargets.Select(id => _entityNamesById.GetValueOrDefault(id, id));
		_selectedTargetsLabel.Text = $"{progress}\nSelected: {string.Join(", ", selectedNames)}";
	}

	// ---------------------------------------------------------------
	// Combat log
	// ---------------------------------------------------------------

	private void OnEntityDamaged(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, bool isCriticalHit, int oldHp, int newHp) =>
		UiEventQueue.Enqueue(() =>
		{
			var prefix = string.IsNullOrEmpty(sourceName) ? "" : $"{sourceName} ";
			AddLogEntry($"{prefix}hit {targetName} for {amount} damage.{(isCriticalHit ? " Critical!" : "")}");
		});

	private void OnEntityHealed(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, int oldHp, int newHp) =>
		UiEventQueue.Enqueue(() =>
		{
			var prefix = string.IsNullOrEmpty(sourceName) ? "" : $"{sourceName} ";
			AddLogEntry($"{prefix}healed {targetName} for {amount}.");
		});

	private void OnEntityTpChanged(string entityId, string entityName, int oldTp, int newTp, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName}: TP {oldTp} → {newTp}"));

	private void OnEntityRevived(string entityId, string entityName, int oldHp, int newHp, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName} was revived! HP {oldHp} → {newHp}"));

	private void OnBuffDebuffApplied(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, int oldValue, int newValue, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(
			$"{entityName}: {stat} {(isPositive ? "up" : "down")} for {(untilRemoved ? "until removed" : $"{roundsRemaining} round{(roundsRemaining == 1 ? "" : "s")}")} ({oldValue} → {newValue}) (from {sourceName})"));

	private void OnBuffDebuffTicked(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(
			$"{entityName}: {stat} {(isPositive ? "buff" : "debuff")} — {roundsRemaining} round{(roundsRemaining == 1 ? "" : "s")} left"));

	private void OnBuffDebuffExpired(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int oldValue, int newValue, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(
			$"{entityName}: {stat} {(isPositive ? "buff" : "debuff")} wore off ({oldValue} → {newValue})"));

	private void OnRegenDrainApplied(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(
			$"{entityName}: {stat} {(isPositive ? "regen" : "drain")} for {(untilRemoved ? "until removed" : $"{roundsRemaining} round{(roundsRemaining == 1 ? "" : "s")}")} (from {sourceName})"));

	private void OnRegenDrainTicked(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(
			$"{entityName}: {stat} {(isPositive ? "regen" : "drain")} — {roundsRemaining} round{(roundsRemaining == 1 ? "" : "s")} left"));

	private void OnRegenDrainExpired(string entityId, string entityName, RegenDrainStat stat, bool isPositive, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(
			$"{entityName}: {stat} {(isPositive ? "regen" : "drain")} wore off"));

	private void OnActionResolved(CombatCommand cmd, string actorName, IReadOnlyList<string> targetNames) =>
		UiEventQueue.Enqueue(() =>
			AddLogEntry($"{actorName} used {cmd.SourceName} on {string.Join(", ", targetNames)} (cost {cmd.TPCost} TP)."));

	private void OnCombatOver(bool playerWon) =>
		UiEventQueue.Enqueue(() =>
		{
			_resultLabel.Text = playerWon ? "Victory!" : "Defeat...";
			_resultModal.PopupCentered();
		});

	private void AddLogEntry(string text)
	{
		var label = new Label();
		label.Text         = text;
		label.AutowrapMode = TextServer.AutowrapMode.Word;
		_logContainer.AddChild(label);
		CallDeferred(nameof(ScrollLogToBottom));
	}

	private void ScrollLogToBottom() =>
		_logScroll.ScrollVertical = (int)_logScroll.GetVScrollBar().MaxValue;

	private void OnQuitBattlePressed()
	{
		UnsubscribeAll();
		UiEventQueue.Clear();
		CallDeferred(nameof(GoToMainMenu));
	}

	private void UnsubscribeAll()
	{
		CombatEventBus.RoundStarted           -= OnRoundStarted;
		CombatEventBus.RoundEnded             -= OnRoundEnded;
		CombatEventBus.TurnStarted            -= OnTurnStarted;
		CombatEventBus.TurnEnded              -= OnTurnEnded;
		CombatEventBus.WaitingForTurn          -= OnWaitingForTurn;
		CombatEventBus.TargetSelectionRequested -= OnTargetSelectionRequested;
		CombatEventBus.EntityDamaged           -= OnEntityDamaged;
		CombatEventBus.EntityHealed            -= OnEntityHealed;
		CombatEventBus.EntityTpChanged        -= OnEntityTpChanged;
		CombatEventBus.EntityRevived          -= OnEntityRevived;
		CombatEventBus.BuffDebuffApplied      -= OnBuffDebuffApplied;
		CombatEventBus.BuffDebuffTicked       -= OnBuffDebuffTicked;
		CombatEventBus.BuffDebuffExpired      -= OnBuffDebuffExpired;
		CombatEventBus.RegenDrainApplied      -= OnRegenDrainApplied;
		CombatEventBus.RegenDrainTicked       -= OnRegenDrainTicked;
		CombatEventBus.RegenDrainExpired      -= OnRegenDrainExpired;
		CombatEventBus.ActionResolved         -= OnActionResolved;
		CombatEventBus.CombatOver             -= OnCombatOver;
	}

	private void GoToMainMenu()
	{
		GameEngineClass.Instance.EndSkirmish();
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}
}
