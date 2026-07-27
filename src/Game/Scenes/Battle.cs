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
		CombatEventBus.WaitingForPlayerAction += OnWaitingForPlayerAction;
		CombatEventBus.TargetSelectionRequested += OnTargetSelectionRequested;
		CombatEventBus.EntityHpChanged        += OnEntityHpChanged;
		CombatEventBus.EntityTpChanged        += OnEntityTpChanged;
		CombatEventBus.ActionResolved         += OnActionResolved;
		CombatEventBus.CombatOver             += OnCombatOver;

		GameEngineClass.Instance.BeginSkirmishCombat();
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

	private void OnRoundStarted(int round, IReadOnlyList<string> turnOrderIds, IReadOnlyList<string> turnOrderNames)
	{
		_roundLabel.Text = $"Round {round}";
		AddLogEntry($"--- Round {round} Started! ---");
		AddLogEntry("Turn order: " + string.Join(", ", turnOrderNames));
	}

	private void OnRoundEnded(int round) =>
		AddLogEntry($"--- Round {round} ended ---");

	private void OnTurnStarted(string entityId, string entityName) =>
		AddLogEntry($"{entityName}'s turn.");

	private void OnTurnEnded(string entityId, string entityName) =>
		AddLogEntry($"{entityName}'s turn ended.");

	private void OnWaitingForPlayerAction(string entityId, string entityName, int currentTp) =>
		PopulateAndShowModal(entityId, currentTp);

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

		if (adventurer.CanFight)
		{
			var fightBtn = new Button();
			fightBtn.Text = "Fight";

			var capturedActorId = entityId;
			fightBtn.Pressed += () => OnFightSelected(capturedActorId);

			_techButtonContainer.AddChild(fightBtn);
		}

		_actionModal.Title = adventurer.Name;
		_actionModal.PopupCentered();
	}

	private void OnTechSelected(string actorId, string techId)
	{
		_actionModal.Hide();
		var cmd = GameEngineClass.Instance.MakeCombatCommand(actorId, techId);
		CombatEngineClass.Instance.SubmitPlayerCommand(cmd);
	}

	private void OnFightSelected(string actorId)
	{
		_actionModal.Hide();
		var cmd = GameEngineClass.Instance.MakeFightCommand(actorId);
		CombatEngineClass.Instance.SubmitPlayerCommand(cmd);
	}

	private void OnTargetSelectionRequested(string actorId, string actorName, TargetingType targetingType, IReadOnlyList<string> validTargetIds, IReadOnlyList<string> validTargetNames)
	{
		foreach (Node child in _targetButtonContainer.GetChildren())
			child.QueueFree();

		var entitiesById = CombatEngineClass.Instance.GetLivingEntities().ToDictionary(e => e.EntityId);

		foreach (var targetId in validTargetIds)
		{
			var btn = new Button();
			btn.Text = entitiesById.TryGetValue(targetId, out var entity) ? entity.Name : targetId;

			var capturedTargetId = targetId;
			btn.Pressed += () => OnTargetChosen(capturedTargetId);

			_targetButtonContainer.AddChild(btn);
		}

		_targetModal.PopupCentered();
	}

	private void OnTargetChosen(string targetId)
	{
		_targetModal.Hide();
		CombatEngineClass.Instance.SubmitPlayerTargets(new List<string> { targetId });
	}

	private void OnEntityHpChanged(string entityId, string entityName, int oldHp, int newHp) =>
		AddLogEntry($"{entityName}: HP {oldHp} → {newHp}");

	private void OnEntityTpChanged(string entityId, string entityName, int oldTp, int newTp) =>
		AddLogEntry($"{entityName}: TP {oldTp} → {newTp}");

	private void OnActionResolved(CombatCommand cmd, string actorName)
	{
		var entitiesById = CombatEngineClass.Instance.GetLivingEntities().ToDictionary(e => e.EntityId);

		string ActorOrTargetName(string id) =>
			entitiesById.TryGetValue(id, out var entity) ? entity.Name : id;

		var targetNames = string.Join(", ", cmd.ChosenTargets.Select(ActorOrTargetName));
		var effectSummary = string.Join(", ", cmd.DirectEffects.Select(e =>
			e.Element.HasValue ? $"{e.EffectType} ({e.Element})" : e.EffectType.ToString()));

		AddLogEntry($"{actorName} used {effectSummary} on {targetNames} (cost {cmd.TPCost} TP).");
	}

	private void OnCombatOver(bool playerWon)
	{
		_resultLabel.Text = playerWon ? "Victory!" : "Defeat...";
		_resultModal.PopupCentered();
	}

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
		CallDeferred(nameof(GoToMainMenu));
	}

	private void UnsubscribeAll()
	{
		CombatEventBus.RoundStarted           -= OnRoundStarted;
		CombatEventBus.RoundEnded             -= OnRoundEnded;
		CombatEventBus.TurnStarted            -= OnTurnStarted;
		CombatEventBus.TurnEnded              -= OnTurnEnded;
		CombatEventBus.WaitingForPlayerAction -= OnWaitingForPlayerAction;
		CombatEventBus.TargetSelectionRequested -= OnTargetSelectionRequested;
		CombatEventBus.EntityHpChanged        -= OnEntityHpChanged;
		CombatEventBus.EntityTpChanged        -= OnEntityTpChanged;
		CombatEventBus.ActionResolved         -= OnActionResolved;
		CombatEventBus.CombatOver             -= OnCombatOver;
	}

	private void GoToMainMenu()
	{
		GameEngineClass.Instance.EndSkirmish();
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}
}
