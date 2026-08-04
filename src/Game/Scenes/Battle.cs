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

	private Label         _roundLabel          = null!;
	private VBoxContainer _enemiesColumn       = null!;
	private VBoxContainer _alliesColumn        = null!;
	private ScrollContainer _logScroll         = null!;
	private VBoxContainer _logContainer        = null!;
	private Window        _actionModal         = null!;
	private VBoxContainer _techButtonContainer = null!;
	private Window        _targetModal         = null!;
	private VBoxContainer _targetButtonContainer = null!;
	private Window        _resultModal         = null!;
	private Label         _resultLabel         = null!;

	private List<string> _pendingChosenTargets = new();
	private int  _pendingNumAttacks;
	private bool _pendingAllowMultipleAttackOnSameTarget;
	private readonly Dictionary<string, Button> _targetButtonsById = new();

	public override void _Ready()
	{
		_roundLabel          = GetNode<Label>("VBoxContainer/HeaderRow/RoundLabel");
		_enemiesColumn       = GetNode<VBoxContainer>("VBoxContainer/ContentArea/EnemiesColumn");
		_alliesColumn        = GetNode<VBoxContainer>("VBoxContainer/ContentArea/AlliesColumn");
		_logScroll           = GetNode<ScrollContainer>("VBoxContainer/ContentArea/LogPanel/LogScroll");
		_logContainer        = GetNode<VBoxContainer>("VBoxContainer/ContentArea/LogPanel/LogScroll/LogContainer");
		_actionModal         = GetNode<Window>("ActionModal");
		_techButtonContainer = GetNode<VBoxContainer>("ActionModal/ModalVBox/TechButtonContainer");
		_targetModal         = GetNode<Window>("TargetModal");
		_targetButtonContainer = GetNode<VBoxContainer>("TargetModal/ModalVBox/TargetButtonContainer");
		_resultModal         = GetNode<Window>("ResultModal");
		_resultLabel         = GetNode<Label>("ResultModal/ModalVBox/ResultLabel");

		UiEventQueue.Clear();

		GetNode<Button>("VBoxContainer/HeaderRow/QuitBattleButton").Pressed += OnQuitBattlePressed;
		GetNode<Button>("ResultModal/ModalVBox/ReturnToMenuButton").Pressed += OnQuitBattlePressed;
		_actionModal.CloseRequested += () => _actionModal.Hide();
		_targetModal.CloseRequested += () => _targetModal.Hide();
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
		}

		foreach (var seed in startData.Allies)
		{
			var card = cardScene.Instantiate<CombatantCard>();
			_alliesColumn.AddChild(card);
			card.Initialize(seed, showTp: true);
		}
	}

	private void OnRoundStarted(int round, IReadOnlyList<string> turnOrderIds, IReadOnlyList<string> turnOrderNames) =>
		UiEventQueue.Enqueue(() =>
		{
			_roundLabel.Text = $"Round {round}";
			AddLogEntry($"--- Round {round} Started! ---");
			AddLogEntry("Turn order: " + string.Join(", ", turnOrderNames));
		});

	private void OnRoundEnded(int round) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"--- Round {round} ended ---"));

	private void OnTurnStarted(string entityId, string entityName) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName}'s turn."));

	private void OnTurnEnded(string entityId, string entityName) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName}'s turn ended."));

	private void OnWaitingForTurn(string entityId, string entityName, int currentTp, bool isAlly)
	{
		if (isAlly)
		{
			PopulateAndShowModal(entityId, currentTp);
			return;
		}

		var cmd = GameEngineClass.Instance.ChooseAiCommand(entityId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void PopulateAndShowModal(string entityId, int currentTp)
	{
		foreach (Node child in _techButtonContainer.GetChildren())
			child.QueueFree();

		var adventurer = GameEngineClass.Instance.AllAdventurers.Lookup(entityId);

		foreach (var techId in adventurer.TechsIds)
		{
			var tech = GameEngineClass.Instance.AllTechs.Lookup(techId);
			var btn  = new Button();
			btn.Text     = $"{tech.Name}  (TP: {tech.TpCost})";
			btn.Disabled = currentTp < tech.TpCost;

			var capturedActorId = entityId;
			var capturedTechId  = techId;
			btn.Pressed += () => OnTechSelected(capturedActorId, capturedTechId);

			_techButtonContainer.AddChild(btn);
		}

		if (adventurer.CanUseFightAction)
		{
			var fightBtn = new Button();
			fightBtn.Text = "Fight";

			var capturedActorId = entityId;
			fightBtn.Pressed += () => OnFightSelected(capturedActorId);

			_techButtonContainer.AddChild(fightBtn);
		}

		foreach (var itemId in adventurer.ItemIds)
		{
			var item      = GameEngineClass.Instance.AllItems.Lookup(itemId);
			int remaining = GameEngineClass.Instance.GetRemainingItemUses(entityId, itemId);
			var btn       = new Button();
			btn.Text     = $"{item.Name}  ({remaining}/{item.MaxUses})";
			btn.Disabled = remaining <= 0;

			var capturedActorId = entityId;
			var capturedItemId  = itemId;
			btn.Pressed += () => OnItemSelected(capturedActorId, capturedItemId);

			_techButtonContainer.AddChild(btn);
		}

		_actionModal.Title = adventurer.Name;
		_actionModal.PopupCentered();
	}

	private void OnTechSelected(string actorId, string techId)
	{
		_actionModal.Hide();
		var cmd = GameEngineClass.Instance.MakeTechCommand(actorId, techId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void OnItemSelected(string actorId, string itemId)
	{
		_actionModal.Hide();
		var cmd = GameEngineClass.Instance.UseItem(actorId, itemId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void OnFightSelected(string actorId)
	{
		_actionModal.Hide();
		var cmd = GameEngineClass.Instance.MakeFightCommand(actorId);
		CombatEngineClass.Instance.SubmitCommand(cmd);
	}

	private void OnTargetSelectionRequested(string actorId, string actorName, TargetingType targetingType, IReadOnlyList<string> validTargetIds, IReadOnlyList<string> validTargetNames, int numAttacks, bool allowMultipleAttackOnSameTarget)
	{
		foreach (Node child in _targetButtonContainer.GetChildren())
			child.QueueFree();
		_targetButtonsById.Clear();

		_pendingChosenTargets = new List<string>();
		_pendingNumAttacks = numAttacks;
		_pendingAllowMultipleAttackOnSameTarget = allowMultipleAttackOnSameTarget;

		var namesById = validTargetIds.Zip(validTargetNames, (id, name) => (id, name))
			.ToDictionary(p => p.id, p => p.name);

		foreach (var targetId in validTargetIds)
		{
			var btn = new Button();
			btn.Text = namesById.TryGetValue(targetId, out var name) ? name : targetId;

			var capturedTargetId = targetId;
			btn.Pressed += () => OnTargetChosen(capturedTargetId);

			_targetButtonContainer.AddChild(btn);
			_targetButtonsById[targetId] = btn;
		}

		UpdateTargetModalTitle();
		_targetModal.PopupCentered();
	}

	private void OnTargetChosen(string targetId)
	{
		_pendingChosenTargets.Add(targetId);

		if (!_pendingAllowMultipleAttackOnSameTarget && _targetButtonsById.TryGetValue(targetId, out var pickedBtn))
			pickedBtn.Disabled = true;

		if (_pendingChosenTargets.Count >= _pendingNumAttacks)
		{
			_targetModal.Hide();
			CombatEngineClass.Instance.SubmitTargets(_pendingChosenTargets);
			return;
		}

		UpdateTargetModalTitle();
	}

	private void UpdateTargetModalTitle() =>
		_targetModal.Title = _pendingNumAttacks > 1
			? $"Choose target ({_pendingChosenTargets.Count + 1}/{_pendingNumAttacks})"
			: "Choose target";

	private void OnEntityDamaged(string targetId, string targetName, int amount, string sourceId, string sourceName, bool isCriticalHit, int oldHp, int newHp) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{targetName}: HP {oldHp} → {newHp}"));

	private void OnEntityHealed(string targetId, string targetName, int amount, string sourceId, string sourceName, int oldHp, int newHp) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{targetName}: HP {oldHp} → {newHp}"));

	private void OnEntityTpChanged(string entityId, string entityName, int oldTp, int newTp) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName}: TP {oldTp} → {newTp}"));

	private void OnEntityRevived(string entityId, string entityName, int oldHp, int newHp) =>
		UiEventQueue.Enqueue(() => AddLogEntry($"{entityName} was revived! HP {oldHp} → {newHp}"));

	private void OnActionResolved(CombatCommand cmd, string actorName, IReadOnlyList<string> targetNames) =>
		UiEventQueue.Enqueue(() =>
		{
			var effectSummary = cmd.Parameters.Element.HasValue
				? $"{cmd.CombatFunction} ({cmd.Parameters.Element})"
				: cmd.CombatFunction;

			AddLogEntry($"{actorName} used {effectSummary} on {string.Join(", ", targetNames)} (cost {cmd.TPCost} TP).");
		});

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
		CombatEventBus.ActionResolved         -= OnActionResolved;
		CombatEventBus.CombatOver             -= OnCombatOver;
	}

	private void GoToMainMenu()
	{
		GameEngineClass.Instance.EndSkirmish();
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}
}
