using Godot;
using GameEngine.Engine;
using GameEngine.Enums;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public partial class DataBrowser : Control
{
	private ItemList _objectList;
	private VBoxContainer _propertyContainer;
	private object[] _currentObjects = [];

	public override void _Ready()
	{
		_objectList = GetNode<ItemList>("VBoxContainer/HSplitContainer/InnerSplit/ObjectList");
		_propertyContainer = GetNode<VBoxContainer>("VBoxContainer/HSplitContainer/InnerSplit/PropertyScroll/PropertyContainer");

		GameEventBus.StateChanged += OnStateChanged;
		GetNode<Button>("VBoxContainer/BackButton").Pressed += OnBackPressed;

		var cats = GetNode("VBoxContainer/HSplitContainer/CategoryPanel");
		cats.GetNode<Button>("TechsButton").Pressed          += () => LoadCategory(GameEngineClass.Instance.AllTechs);
		cats.GetNode<Button>("ItemsButton").Pressed          += () => LoadCategory(GameEngineClass.Instance.AllItems);
		cats.GetNode<Button>("MonstersButton").Pressed       += () => LoadCategory(GameEngineClass.Instance.AllMonsters);
		cats.GetNode<Button>("MonsterActionsButton").Pressed += () => LoadCategory(GameEngineClass.Instance.AllMonsterActions);
		cats.GetNode<Button>("DungeonsButton").Pressed       += () => LoadCategory(GameEngineClass.Instance.AllDungeons);
		cats.GetNode<Button>("AdventurersButton").Pressed    += () => LoadCategory(GameEngineClass.Instance.AllAdventurers);

		_objectList.ItemSelected += OnObjectSelected;
	}

	private void OnStateChanged(GameFlowState newState)
	{
		if (newState != GameFlowState.MainMenu) return;
		GameEventBus.StateChanged -= OnStateChanged;
		CallDeferred(nameof(GoToMainMenu));
	}

	private void GoToMainMenu() => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	private void OnBackPressed() => GameEngineClass.Instance.CloseDataBrowser();

	private void LoadCategory(IEnumerable<object> objects)
	{
		_currentObjects = objects.ToArray();
		_objectList.Clear();
		ClearProperties();
		foreach (var obj in _currentObjects)
			_objectList.AddItem(GetDisplayName(obj));
	}

	private void OnObjectSelected(long index)
	{
		ClearProperties();
		if (index < 0 || index >= _currentObjects.Length) return;
		ShowProperties(_currentObjects[index]);
	}

	private void ClearProperties()
	{
		foreach (Node child in _propertyContainer.GetChildren())
			child.QueueFree();
	}

	private void ShowProperties(object obj)
	{
		foreach (var prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
			AddProperty(prop.Name, FormatValue(prop.GetValue(obj)));
	}

	private void AddProperty(string name, string value)
	{
		var label = new Label();
		label.Text = $"{name}: {value}";
		label.AutowrapMode = TextServer.AutowrapMode.Word;
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_propertyContainer.AddChild(label);
	}

	private static string GetDisplayName(object obj)
	{
		return obj.GetType().GetProperty("Name")?.GetValue(obj)?.ToString()
			?? obj.GetType().GetProperty("Id")?.GetValue(obj)?.ToString()
			?? obj.ToString()
			?? "";
	}

	private static string FormatValue(object? value) => value switch
	{
		null       => "",
		string s   => s,
		IEnumerable list => string.Join(", ", list.Cast<object>().Select(i => i?.ToString() ?? "")),
		_          => value.ToString() ?? ""
	};
}
