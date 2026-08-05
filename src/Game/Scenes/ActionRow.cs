using Godot;
using System;

// One row in the action pane - a Tech, Fight, or Item. Battle builds one of these per available
// action, in the same order the old modal used: techs, then Fight, then items.
public partial class ActionRow : PanelContainer
{
	private Button _actionButton      = null!;
	private Label  _descriptionLabel  = null!;
	private Label  _bonusesLabel      = null!;

	public event Action? Pressed;

	public override void _Ready()
	{
		_actionButton     = GetNode<Button>("VBox/ActionButton");
		_descriptionLabel = GetNode<Label>("VBox/DescriptionLabel");
		_bonusesLabel     = GetNode<Label>("VBox/BonusesLabel");
		_actionButton.Pressed += () => Pressed?.Invoke();
	}

	public void Initialize(string title, string description, string bonuses, bool disabled)
	{
		_actionButton.Text     = title;
		_actionButton.Disabled = disabled;

		_descriptionLabel.Text    = description;
		_descriptionLabel.Visible = !string.IsNullOrEmpty(description);

		_bonusesLabel.Text    = bonuses;
		_bonusesLabel.Visible = !string.IsNullOrEmpty(bonuses);
	}

	// Toggled while the pane is showing the prompt but the player is picking targets rather than
	// an action - keeps every row visible (so the picked action stays identifiable) but inert.
	public void SetInteractable(bool interactable) => _actionButton.Disabled = !interactable;
}
