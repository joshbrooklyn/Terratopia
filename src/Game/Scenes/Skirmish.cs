using Godot;
using GameEngine;
using GameEngine.Engine;
using GameEngine.Enums;
using System.Collections.Generic;
using System.Linq;

public partial class Skirmish : Control
{
	private ItemList _adventurerList = null!;
	private ItemList _monsterList = null!;
	private ItemList _selectedMonstersList = null!;
	private Button _startCombatButton = null!;

	private readonly List<string> _selectedMonsterIds = new();

	public override void _Ready()
	{
		_adventurerList = GetNode<ItemList>("VBoxContainer/HSplitContainer/AdventurerPanel/AdventurerList");
		_monsterList = GetNode<ItemList>("VBoxContainer/HSplitContainer/MonsterPanel/MonsterList");
		_selectedMonstersList = GetNode<ItemList>("VBoxContainer/HSplitContainer/MonsterPanel/SelectedMonstersList");
		_startCombatButton = GetNode<Button>("VBoxContainer/ButtonRow/StartCombatButton");

		_adventurerList.SelectMode = ItemList.SelectModeEnum.Multi;

		GameEventBus.StateChanged += OnStateChanged;
		GetNode<Button>("VBoxContainer/ButtonRow/BackButton").Pressed += OnBackPressed;
		_startCombatButton.Pressed += OnStartCombatPressed;

		_adventurerList.MultiSelected += OnAdventurerMultiSelected;
		_monsterList.ItemSelected += OnMonsterAdded;
		_selectedMonstersList.ItemSelected += OnSelectedMonsterClicked;

		PopulateAdventurers();
		PopulateMonsters();
		RefreshSelectedMonsters();
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
			_adventurerList.Deselect((int)index);
		UpdateStartButtonState();
	}

	private void OnMonsterAdded(long index)
	{
		_monsterList.DeselectAll();
		if (_selectedMonsterIds.Count >= 4) return;

		var monsterId = (string)_monsterList.GetItemMetadata((int)index);
		_selectedMonsterIds.Add(monsterId);
		RefreshSelectedMonsters();
		UpdateStartButtonState();
	}

	private void OnSelectedMonsterClicked(long index)
	{
		var monsterId = (string)_selectedMonstersList.GetItemMetadata((int)index);
		int removeAt = _selectedMonsterIds.LastIndexOf(monsterId);
		if (removeAt >= 0) _selectedMonsterIds.RemoveAt(removeAt);
		RefreshSelectedMonsters();
		UpdateStartButtonState();
	}

	private void RefreshSelectedMonsters()
	{
		_selectedMonstersList.Clear();
		var allMonsters = GameEngineClass.Instance.AllMonsters;
		foreach (var group in _selectedMonsterIds.GroupBy(id => id))
		{
			var name = allMonsters.Lookup(group.Key).Name;
			int idx = _selectedMonstersList.AddItem($"{name} x{group.Count()}");
			_selectedMonstersList.SetItemMetadata(idx, group.Key);
		}
	}

	private void UpdateStartButtonState()
	{
		_startCombatButton.Disabled =
			_adventurerList.GetSelectedItems().Length == 0 || _selectedMonsterIds.Count == 0;
	}

	private void OnStartCombatPressed()
	{
		var adventurerIds = _adventurerList.GetSelectedItems()
			.Select(i => (string)_adventurerList.GetItemMetadata(i))
			.ToList();

		GameEngineClass.Instance.SelectedAdventurerIds = adventurerIds;
		GameEngineClass.Instance.SelectedMonsterIds = new List<string>(_selectedMonsterIds);

		GameEventBus.StateChanged -= OnStateChanged;
		GetTree().ChangeSceneToFile("res://Scenes/Battle.tscn");
	}
}
