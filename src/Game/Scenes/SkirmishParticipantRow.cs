using Godot;
using System;

public partial class SkirmishParticipantRow : HBoxContainer
{
	public string ParticipantId { get; private set; } = "";

	private Label _nameLabel = null!;
	private SpinBox _levelSpinBox = null!;
	private Button _removeButton = null!;

	public event Action<SkirmishParticipantRow>? RemoveRequested;

	public override void _Ready()
	{
		_nameLabel = GetNode<Label>("NameLabel");
		_levelSpinBox = GetNode<SpinBox>("LevelSpinBox");
		_removeButton = GetNode<Button>("RemoveButton");
		_removeButton.Pressed += () => RemoveRequested?.Invoke(this);
	}

	public void Initialize(string participantId, string displayName, int level, bool showRemoveButton)
	{
		ParticipantId = participantId;
		_nameLabel.Text = displayName;
		_levelSpinBox.Value = level;
		_removeButton.Visible = showRemoveButton;
	}

	public void SetDisplayName(string displayName) => _nameLabel.Text = displayName;

	public int Level
	{
		get => (int)_levelSpinBox.Value;
		set => _levelSpinBox.Value = value;
	}
}
