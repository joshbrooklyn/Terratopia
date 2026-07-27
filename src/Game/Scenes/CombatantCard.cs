using CombatEngine;
using GameEngine.DataClasses;
using Godot;

public partial class CombatantCard : PanelContainer
{
	private string _entityId = "";
	private Label  _evadedLabel = null!;
	private Label  _damageLabel = null!;

	public void Initialize(CombatantSeed seed, bool showTp)
	{
		_entityId = seed.EntityId;
		_evadedLabel = GetNode<Label>("EvadedLabel");
		_damageLabel = GetNode<Label>("DamageLabel");

		GetNode<Label>("VBoxContainer/NameLabel").Text     = seed.Name;
		GetNode<Label>("VBoxContainer/LevelLabel").Text   = $"Level: {seed.Level}";
		GetNode<Label>("VBoxContainer/PowerLabel").Text   = $"PWR: {seed.Power}";
		GetNode<Label>("VBoxContainer/DefenseLabel").Text = $"DEF: {seed.Defense}";
		GetNode<Label>("VBoxContainer/SpeedLabel").Text   = $"SPD: {seed.Speed}";
		GetNode<Label>("VBoxContainer/EvasionLabel").Text = $"EVA: {seed.Evasion:P0}";
		GetNode<Label>("VBoxContainer/CritLabel").Text    = $"CRIT: {seed.CritChance:P0}";

		var statContainer = GetNode<VBoxContainer>("VBoxContainer/StatContainer");

		var hpScene = GD.Load<PackedScene>("res://Scenes/HpStatDisplay.tscn");
		var hp      = hpScene.Instantiate<HpStatDisplay>();
		statContainer.AddChild(hp);
		hp.Initialize(seed.EntityId, seed.Hp, seed.MaxHp);

		if (showTp)
		{
			var tpScene = GD.Load<PackedScene>("res://Scenes/TpStatDisplay.tscn");
			var tp      = tpScene.Instantiate<TpStatDisplay>();
			statContainer.AddChild(tp);
			tp.Initialize(seed.EntityId, seed.Tp, seed.MaxTp);
		}

		CombatEventBus.AttackEvaded += OnAttackEvaded;
		CombatEventBus.EntityDamaged += OnEntityDamaged;
		CombatEventBus.EntityDeath += OnEntityDeath;
	}

	public override void _ExitTree()
	{
		CombatEventBus.AttackEvaded -= OnAttackEvaded;
		CombatEventBus.EntityDamaged -= OnEntityDamaged;
		CombatEventBus.EntityDeath -= OnEntityDeath;
	}

	private void OnAttackEvaded(string attackerId, string attackerName, string targetId, string targetName)
	{
		if (targetId != _entityId) return;

		_evadedLabel.Modulate = new Color(1, 1, 1, 1);
		_evadedLabel.Position = Vector2.Zero;

		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(_evadedLabel, "position:y", -20f, 0.8f);
		tween.TweenProperty(_evadedLabel, "modulate:a", 0f, 0.8f);
	}

	private void OnEntityDamaged(string targetId, string targetName, int amount, string sourceId, string sourceName, bool isCriticalHit)
	{
		if (targetId != _entityId) return;

		_damageLabel.Text = $"-{amount}";
		_damageLabel.Modulate = isCriticalHit ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(1, 1, 1, 1);
		_damageLabel.Position = Vector2.Zero;

		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(_damageLabel, "position:y", -20f, 0.8f);
		tween.TweenProperty(_damageLabel, "modulate:a", 0f, 0.8f);

		Modulate = new Color(1f, 0.3f, 0.3f, 1f);
		var flashTween = CreateTween();
		flashTween.TweenProperty(this, "modulate", new Color(1, 1, 1, 1), 0.25f);
	}

	private void OnEntityDeath(string entityId, string entityName)
	{
		if (entityId != _entityId) return;

		Modulate = new Color(0.4f, 0.4f, 0.4f, 1f);
	}
}
