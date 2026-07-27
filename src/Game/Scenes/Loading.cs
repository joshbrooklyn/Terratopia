using Godot;
using GameEngine.Engine;
using GameEngine.Enums;

public partial class Loading : Control
{
	private Label _label;
	private float _elapsed;
	private static readonly string[] Frames = ["Loading", "Loading.", "Loading..", "Loading..."];

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");
		GameEventBus.StateChanged += OnStateChanged;
	}

	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		_label.Text = Frames[(int)(_elapsed / 0.4f) % Frames.Length];
	}

	private void OnStateChanged(GameFlowState newState)
	{
		if (newState != GameFlowState.MainMenu) return;
		GameEventBus.StateChanged -= OnStateChanged;
		CallDeferred(nameof(GoToMainMenu));
	}

	private void GoToMainMenu() => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
}
