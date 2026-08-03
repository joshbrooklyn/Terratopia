using Godot;
using GameEngine;
using GameEngine.DataClasses;
using GameEngine.Engine;
using GameEngine.Enums;
using System.Collections.Generic;
using System.Linq;

public partial class Skirmish : Control
{
	private ItemList _adventurerList = null!;
	private ItemList _monsterList = null!;
	private VBoxContainer _selectedAdventurerRows = null!;
	private VBoxContainer _selectedMonsterRows = null!;
	private SpinBox _applyToAllSpinBox = null!;
	private Button _applyToAllButton = null!;
	private Button _startCombatButton = null!;
	private PackedScene _rowScene = null!;

	private readonly Dictionary<string, SkirmishParticipantRow> _adventurerRowsById = new();
	private readonly List<SkirmishParticipantRow> _monsterRows = new();

	public override void _Ready()
	{
		_adventurerList = GetNode<ItemList>("VBoxContainer/HSplitContainer/AdventurerPanel/AdventurerList");
		_selectedAdventurerRows = GetNode<VBoxContainer>("VBoxContainer/HSplitContainer/AdventurerPanel/SelectedAdventurerRows");
		_monsterList = GetNode<ItemList>("VBoxContainer/HSplitContainer/MonsterPanel/MonsterList");
		_selectedMonsterRows = GetNode<VBoxContainer>("VBoxContainer/HSplitContainer/MonsterPanel/SelectedMonsterRows");
		_applyToAllSpinBox = GetNode<SpinBox>("VBoxContainer/ApplyToAllRow/ApplyToAllSpinBox");
		_applyToAllButton = GetNode<Button>("VBoxContainer/ApplyToAllRow/ApplyToAllButton");
		_startCombatButton = GetNode<Button>("VBoxContainer/ButtonRow/StartCombatButton");

		_rowScene = GD.Load<PackedScene>("res://Scenes/SkirmishParticipantRow.tscn");

		_adventurerList.SelectMode = ItemList.SelectModeEnum.Multi;

		GameEventBus.StateChanged += OnStateChanged;
		GetNode<Button>("VBoxContainer/ButtonRow/BackButton").Pressed += OnBackPressed;
		_startCombatButton.Pressed += OnStartCombatPressed;

		_adventurerList.MultiSelected += OnAdventurerMultiSelected;
		_monsterList.ItemSelected += OnMonsterAdded;
		_applyToAllButton.Pressed += OnApplyToAllPressed;

		PopulateAdventurers();
		PopulateMonsters();
		UpdateStartButtonState();
	}

	private void OnStateChanged(GameFlowState newState)
	{
		if (newState != GameFlowState.MainMenu) return;
		GameEventBus.StateChanged -= OnStateChanged;
		CallDeferred(nameof(GoToMainMenu));
	}

	private void GoToMainMenu() => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	private void OnBackPressed() => GameEngineClass.Instance.EndSkirmish();

	private void PopulateAdventurers()
	{
		_adventurerList.Clear();
		foreach (var a in GameEngineClass.Instance.AllAdventurers)
		{
			int idx = _adventurerList.AddItem(a.Name);
			_adventurerList.SetItemMetadata(idx, a.AdventurerId);
		}
	}

	private void PopulateMonsters()
	{
		_monsterList.Clear();
		foreach (var m in GameEngineClass.Instance.AllMonsters)
		{
			int idx = _monsterList.AddItem(m.Name);
			_monsterList.SetItemMetadata(idx, m.MonsterId);
		}
	}

	private void OnAdventurerMultiSelected(long index, bool selected)
	{
		if (selected && _adventurerList.GetSelectedItems().Length > 4)
		{
			_adventurerList.Deselect((int)index);
			return;
		}

		var adventurerId = (string)_adventurerList.GetItemMetadata((int)index);
		if (selected) AddAdventurerRow(adventurerId);
		else RemoveAdventurerRow(adventurerId);

		UpdateStartButtonState();
	}

	private void AddAdventurerRow(string adventurerId)
	{
		if (_adventurerRowsById.ContainsKey(adventurerId)) return;

		var adventurer = GameEngineClass.Instance.AllAdventurers.Lookup(adventurerId);
		var row = _rowScene.Instantiate<SkirmishParticipantRow>();
		_selectedAdventurerRows.AddChild(row);
		row.Initialize(adventurerId, adventurer.Name, adventurer.Level, showRemoveButton: false);
		_adventurerRowsById[adventurerId] = row;
	}

	private void RemoveAdventurerRow(string adventurerId)
	{
		if (!_adventurerRowsById.TryGetValue(adventurerId, out var row)) return;
		row.QueueFree();
		_adventurerRowsById.Remove(adventurerId);
	}

	private void OnMonsterAdded(long index)
	{
		_monsterList.DeselectAll();
		if (_monsterRows.Count >= 4) return;

		var monsterId = (string)_monsterList.GetItemMetadata((int)index);
		var monster = GameEngineClass.Instance.AllMonsters.Lookup(monsterId);

		var row = _rowScene.Instantiate<SkirmishParticipantRow>();
		_selectedMonsterRows.AddChild(row);
		row.Initialize(monsterId, monster.Name, monster.Level, showRemoveButton: true);
		row.RemoveRequested += OnMonsterRowRemoveRequested;
		_monsterRows.Add(row);

		RenumberMonsterRows();
		UpdateStartButtonState();
	}

	private void OnMonsterRowRemoveRequested(SkirmishParticipantRow row)
	{
		row.RemoveRequested -= OnMonsterRowRemoveRequested;
		_monsterRows.Remove(row);
		row.QueueFree();

		RenumberMonsterRows();
		UpdateStartButtonState();
	}

	private void RenumberMonsterRows()
	{
		var allMonsters = GameEngineClass.Instance.AllMonsters;
		foreach (var group in _monsterRows.GroupBy(r => r.ParticipantId))
		{
			var name = allMonsters.Lookup(group.Key).Name;
			int i = 1;
			var groupRows = group.ToList();
			foreach (var row in groupRows)
				row.SetDisplayName(groupRows.Count > 1 ? $"{name} #{i++}" : name);
		}
	}

	private void OnApplyToAllPressed()
	{
		int level = (int)_applyToAllSpinBox.Value;
		foreach (var row in _adventurerRowsById.Values) row.Level = level;
		foreach (var row in _monsterRows) row.Level = level;
	}

	private void UpdateStartButtonState()
	{
		_startCombatButton.Disabled = _adventurerRowsById.Count == 0 || _monsterRows.Count == 0;
	}

	private void OnStartCombatPressed()
	{
		GameEngineClass.Instance.SelectedAdventurerSlots = _adventurerRowsById
			.Select(kv => new SkirmishSlot(kv.Key, kv.Value.Level))
			.Select(s => (s.Id, s.Level))
			.ToList();

		GameEngineClass.Instance.SelectedMonsterSlots = _monsterRows
			.Select(row => new SkirmishSlot(row.ParticipantId, row.Level))
			.Select(s => (s.Id, s.Level))
			.ToList();

		GameEngineClass.Instance.StartRun();

		GameEventBus.StateChanged -= OnStateChanged;
		GetTree().ChangeSceneToFile("res://Scenes/Battle.tscn");
	}
}
