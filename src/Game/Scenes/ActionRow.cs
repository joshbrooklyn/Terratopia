using Godot;
using System;

// One row in the action pane - a Tech, Fight, or Item. Battle builds one of these per available
// action, in the same order the old modal used: techs, then Fight, then items.
public partial class ActionRow : PanelContainer
{
	private Button _actionButton      = null!;
	private Label  _descriptionLabel  = null!;
	private Label  _bonusesLabel      = null!;

	private StyleBoxFlat _normalStyle = null!;
	private StyleBoxFlat _hoverStyle  = null!;

	public event Action? Pressed;

	public override void _Ready()
	{
		_actionButton     = GetNode<Button>("VBox/ActionButton");
		_descriptionLabel = GetNode<Label>("VBox/DescriptionLabel");
		_bonusesLabel     = GetNode<Label>("VBox/BonusesLabel");
		_actionButton.Pressed += () => Pressed?.Invoke();

		_normalStyle = (StyleBoxFlat)GetThemeStylebox("panel").Duplicate();
		_hoverStyle  = (StyleBoxFlat)_normalStyle.Duplicate();
		_hoverStyle.BgColor = _hoverStyle.BgColor.Lightened(0.2f);

		// The button's own normal/hover/pressed backgrounds would otherwise draw over the row
		// highlight for just the name - blank them so the shared panel style is the only thing
		// that visibly reacts to hover, uniformly across the whole row.
		var empty = new StyleBoxEmpty();
		_actionButton.AddThemeStyleboxOverride("normal", empty);
		_actionButton.AddThemeStyleboxOverride("hover", empty);
		_actionButton.AddThemeStyleboxOverride("pressed", empty);
		_actionButton.AddThemeStyleboxOverride("focus", empty);
		_actionButton.AddThemeStyleboxOverride("disabled", empty);

		MouseEntered += () => { if (!_actionButton.Disabled) AddThemeStyleboxOverride("panel", _hoverStyle); };
		MouseExited  += () => AddThemeStyleboxOverride("panel", _normalStyle);
	}

	// Lets clicks on the description/bonuses text (which sit outside the ActionButton) also select
	// the row - clicks on the button itself are consumed there and never reach here.
	public override void _GuiInput(InputEvent @event)
	{
		if (_actionButton.Disabled)
			return;

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
			Pressed?.Invoke();
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
	public void SetInteractable(bool interactable)
	{
		_actionButton.Disabled = !interactable;
		if (!interactable)
			AddThemeStyleboxOverride("panel", _normalStyle);
	}
}
