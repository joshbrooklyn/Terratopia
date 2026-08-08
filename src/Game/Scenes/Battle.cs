using System;
using System.Collections.Generic;
using System.Linq;
using CombatEngine;
using CombatEngine.Engine;
using CombatEngine.DataClasses;
using CombatEngine.Enums;
using CombatEngine.Keywords;
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
	private Label         _teamworkLabel        = null!;
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

	private readonly HashSet<string> _allyIds = new();
	private readonly Dictionary<(string ActorId, string SourceId), double> _growthBonuses = new();

	public override void _Ready()
	{
		_roundLabel           = GetNode<Label>("VBoxContainer/ContentArea/CenterPanel/RoundPane/RoundPaneVBox/RoundLabel");
		_turnOrderLabel       = GetNode<Label>("VBoxContainer/ContentArea/CenterPanel/RoundPane/RoundPaneVBox/TurnOrderLabel");
		_teamworkLabel        = GetNode<Label>("VBoxContainer/ContentArea/CenterPanel/TeamworkPane/TeamworkLabel");
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
		CombatEventBus.KeywordApplied          += OnKeywordApplied;
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
		CombatEventBus.TriggeredEffectApplied  += OnTriggeredEffectApplied;
		CombatEventBus.TriggeredEffectRemoved  += OnTriggeredEffectRemoved;
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
			_allyIds.Add(seed.EntityId);
		}

		RenderTeamwork();
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

	// Tracks the Teamwork/Growth stacking-keyword counters as CombatEventBus reports them, so the
	// action pane and combat cards can show a running bonus instead of only the floating "+N%"
	// label that fades after the hit lands.
	private void OnKeywordApplied(string keywordName, string actorId, string actorName, string targetId, string targetName, double bonus, string sourceId, string sourceName, int useCount) =>
		UiEventQueue.Enqueue(() =>
		{
			if (keywordName == TeamworkKeyword.KeywordName && _allyIds.Contains(actorId))
				RenderTeamwork(useCount, bonus);

			if (keywordName != GrowthKeyword.KeywordName) return;

			// Fires once per target hit by the same command; only rebuild cards when the number
			// actually moved.
			var key = (actorId, sourceId);
			if (_growthBonuses.TryGetValue(key, out var cached) && cached == bonus) return;
			_growthBonuses[key] = bonus;
			foreach (var card in _cardsById.Values)
				card.RefreshInventory(_growthBonuses);
		});

	private void RenderTeamwork(int useCount = 0, double bonus = 0.0)
	{
		if (useCount == 0)
		{
			_teamworkLabel.Text = "Teamwork: 0 uses";
			return;
		}
		_teamworkLabel.Text = $"Teamwork: {useCount} use{(useCount == 1 ? "" : "s")} (+{bonus:P0})";
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
		// Queued rather than logged immediately: this branch runs on the engine's own stack while
		// the enemy's "X's turn." line (raised by TurnStarted, just above WaitingForTurn in the
		// flow) is still sitting in the queue - a direct AddLogEntry here would print out of order.
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName} uses {cmd.SourceName}."));
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
				FormatBonuses(entityId, techId, tech.Parameters, tech.Keywords, tech.NumAttacks), currentTp < tech.TpCost);

			var capturedActorId = entityId;
			var capturedTechId  = techId;
			var capturedName    = tech.Name;
			row.Pressed += () => OnTechSelected(capturedActorId, capturedTechId, capturedName);
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
				FormatBonuses(entityId, itemId, item.Parameters, item.Keywords, item.NumAttacks), remaining <= 0);

			var capturedActorId = entityId;
			var capturedItemId  = itemId;
			var capturedName    = item.Name;
			row.Pressed += () => OnItemSelected(capturedActorId, capturedItemId, capturedName);
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
	// not the plain formula, multi-hit count, active keywords (Growth annotated with its current
	// stacked bonus), and one line per buffsDebuffs/regensDrains/triggeredEffectsApplied entry the
	// action carries.
	private string FormatBonuses(string actorId, string sourceId, CombatFunctionParameters parameters, List<string> keywords, int numAttacks)
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
			lines.Add(string.Join(", ", keywords.Select(k => k == GrowthKeyword.KeywordName
				? $"{k} +{_growthBonuses.GetValueOrDefault((actorId, sourceId)):P0}"
				: k)));

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
		foreach (var spec in parameters.TriggeredEffectsApplied ?? [])
			lines.Add($"Grants {spec.TriggeredEffect} ({spec.Target})");

		return string.Join("\n", lines);
	}

	private void OnTechSelected(string actorId, string techId, string techName)
	{
		ClearActionPane();
		AddLogEntry($"{_entityNamesById[actorId]} uses {techName}.");
		var cmd = GameEngineClass.Instance.MakeTechCommand(actorId, techId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void OnItemSelected(string actorId, string itemId, string itemName)
	{
		ClearActionPane();
		AddLogEntry($"{_entityNamesById[actorId]} uses {itemName}.");
		var cmd = GameEngineClass.Instance.UseItem(actorId, itemId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void OnFightSelected(string actorId)
	{
		ClearActionPane();
		AddLogEntry($"{_entityNamesById[actorId]} uses Fight.");
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

	private void OnBuffDebuffExpired(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int oldValue, int newValue, string sourceId, string sourceName, string counteredBySourceId, string counteredBySourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(string.IsNullOrEmpty(counteredBySourceName)
			? $"{entityName}: {stat} {(isPositive ? "buff" : "debuff")} ({sourceName}) wore off ({oldValue} → {newValue})"
			: $"{entityName}: {stat} {(isPositive ? "buff" : "debuff")} ({sourceName}) countered by {counteredBySourceName} ({oldValue} → {newValue})"));

	private void OnRegenDrainApplied(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(
			$"{entityName}: {stat} {(isPositive ? "regen" : "drain")} for {(untilRemoved ? "until removed" : $"{roundsRemaining} round{(roundsRemaining == 1 ? "" : "s")}")} (from {sourceName})"));

	private void OnRegenDrainTicked(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(
			$"{entityName}: {stat} {(isPositive ? "regen" : "drain")} — {roundsRemaining} round{(roundsRemaining == 1 ? "" : "s")} left"));

	private void OnRegenDrainExpired(string entityId, string entityName, RegenDrainStat stat, bool isPositive, string sourceId, string sourceName, string counteredBySourceId, string counteredBySourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry(string.IsNullOrEmpty(counteredBySourceName)
			? $"{entityName}: {stat} {(isPositive ? "regen" : "drain")} ({sourceName}) wore off"
			: $"{entityName}: {stat} {(isPositive ? "regen" : "drain")} ({sourceName}) countered by {counteredBySourceName}"));

	private void OnTriggeredEffectApplied(string entityId, string entityName, string triggeredEffectName, string sourceId, string sourceName) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName} gained {triggeredEffectName} (from {sourceName})"));

	private void OnTriggeredEffectRemoved(string entityId, string entityName, string triggeredEffectName) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName} lost {triggeredEffectName}"));

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
		CombatEventBus.KeywordApplied          -= OnKeywordApplied;
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
		CombatEventBus.TriggeredEffectApplied  -= OnTriggeredEffectApplied;
		CombatEventBus.TriggeredEffectRemoved  -= OnTriggeredEffectRemoved;
		CombatEventBus.CombatOver             -= OnCombatOver;
	}

	private void GoToMainMenu()
	{
		GameEngineClass.Instance.EndSkirmish();
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}
}
