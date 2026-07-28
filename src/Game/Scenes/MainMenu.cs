using Godot;
using GameEngine.Engine;
using GameEngine.Enums;

public partial class MainMenu : Control
{
	public override void _Ready()
	{
		GameEventBus.StateChanged += OnStateChanged;
		GetNode<Button>("VBoxContainer/StartSkirmishButton").Pressed += OnStartSkirmishPressed;
		GetNode<Button>("VBoxContainer/DataBrowserButton").Pressed += OnDataBrowserPressed;
		GetNode<Button>("VBoxContainer/QuitButton").Pressed += OnQuitPressed;
	}

	private void OnStateChanged(GameFlowState newState)
	{
		if (newState == GameFlowState.DataBrowser)
		{
			GameEventBus.StateChanged -= OnStateChanged;
			CallDeferred(nameof(GoToDataBrowser));
		}
		else if (newState == GameFlowState.Skirmish)
		{
			GameEventBus.StateChanged -= OnStateChanged;
			CallDeferred(nameof(GoToSkirmish));
		}
	}

	private void GoToDataBrowser() => GetTree().ChangeSceneToFile("res://Scenes/DataBrowser.tscn");

	private void GoToSkirmish() => GetTree().ChangeSceneToFile("res://Scenes/Skirmish.tscn");

	private void OnStartSkirmishPressed() => GameEngineClass.Instance.StartSkirmish();

	private void OnDataBrowserPressed() => GameEngineClass.Instance.OpenDataBrowser();

	private void OnQuitPressed() => GetTree().Quit();
}
