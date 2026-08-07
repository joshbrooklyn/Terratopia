using System;
using System.Collections.Generic;
using System.Linq;
using CombatEngine;
using CombatEngine.Enums;
using CombatEngine.Keywords;
using GameEngine.DataClasses;
using Godot;

public partial class CombatantCard : PanelContainer
{
	private static readonly Color PositiveColor = new(0.4f, 0.9f, 0.45f);
	private static readonly Color NegativeColor = new(1f, 0.35f, 0.35f);
	private static readonly Color PassiveColor  = new(0.8f, 0.65f, 1f);

	private string _entityId = "";
	private Label  _evadedLabel = null!;
	private Label  _damageLabel = null!;
	private Label  _healLabel   = null!;

	private Label _powerEffLabel   = null!;
	private Label _defenseEffLabel = null!;
	private Label _speedEffLabel   = null!;
	private int   _basePower;
	private int   _baseDefense;
	private int   _baseSpeed;

	private VBoxContainer _effectsContainer   = null!;
	private VBoxContainer _inventoryContainer = null!;
	private IReadOnlyList<CombatantInventoryEntry> _inventoryEntries = [];
	private IReadOnlyDictionary<(string ActorId, string SourceId), double> _growthBonuses =
		new Dictionary<(string, string), double>();

	private StyleBoxFlat _normalStyle     = null!;
	private StyleBoxFlat _activeStyle     = null!;
	private StyleBoxFlat _targetableStyle = null!;
	private bool _isActiveTurn;
	private bool _targetable;

	// Emitted when this card is clicked while SetTargetable(true) is in effect - Battle listens
	// to drive click-to-target selection.
	public event Action<string>? Clicked;

	private readonly Dictionary<BuffDebuffStat, (bool IsPositive, int Rounds, bool UntilRemoved, int Value, string SourceName)> _buffs = new();
	private readonly Dictionary<RegenDrainStat, (bool IsPositive, int Rounds, bool UntilRemoved, string SourceName)> _regens = new();
	// Passives don't expire, so this is just the set of names owned right now - no per-entry state
	// to track like _buffs/_regens. Seeded in Initialize from CombatantSeed.Passives - the one-time
	// setup handoff, same as every other stat on the card - rather than PassiveApplied, since
	// passives granted at combat setup (e.g. Monster.Passives) are applied before this card, or any
	// CombatEventBus subscriber, exists. Mid-combat grants/removals come through
	// PassiveApplied/PassiveRemoved instead.
	private readonly HashSet<string> _passives = new();

	public void Initialize(CombatantSeed seed, bool showTp)
	{
		_entityId = seed.EntityId;
		_evadedLabel = GetNode<Label>("EvadedLabel");
		_damageLabel = GetNode<Label>("DamageLabel");
		_healLabel   = GetNode<Label>("HealLabel");

		GetNode<Label>("Columns/StatsColumn/NameLabel").Text = seed.Name;
		GetNode<Label>("Columns/StatsColumn/LevelLabel").Text = $"Level: {seed.Level}";

		_basePower   = seed.Power;
		_baseDefense = seed.Defense;
		_baseSpeed   = seed.Speed;
		GetNode<Label>("Columns/StatsColumn/PowerRow/PowerBaseLabel").Text     = $"PWR: {seed.Power}";
		GetNode<Label>("Columns/StatsColumn/DefenseRow/DefenseBaseLabel").Text = $"DEF: {seed.Defense}";
		GetNode<Label>("Columns/StatsColumn/SpeedRow/SpeedBaseLabel").Text     = $"SPD: {seed.Speed}";
		_powerEffLabel   = GetNode<Label>("Columns/StatsColumn/PowerRow/PowerEffLabel");
		_defenseEffLabel = GetNode<Label>("Columns/StatsColumn/DefenseRow/DefenseEffLabel");
		_speedEffLabel   = GetNode<Label>("Columns/StatsColumn/SpeedRow/SpeedEffLabel");

		GetNode<Label>("Columns/StatsColumn/EvasionLabel").Text = $"EVA: {seed.Evasion:P0}";
		GetNode<Label>("Columns/StatsColumn/CritLabel").Text    = $"CRIT: {seed.CritChance:P0}";

		var statContainer = GetNode<VBoxContainer>("Columns/StatsColumn/StatContainer");

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

		_effectsContainer   = GetNode<VBoxContainer>("Columns/EffectsColumn/EffectsContainer");
		_inventoryContainer = GetNode<VBoxContainer>("Columns/InventoryColumn/InventoryContainer");
		_inventoryEntries   = seed.Techs.Concat(seed.Items).ToList();
		RenderInventory();

		foreach (var passiveName in seed.Passives)
			_passives.Add(passiveName);
		RenderEffects();

		_normalStyle = (StyleBoxFlat)GetThemeStylebox("panel").Duplicate();

		_activeStyle = (StyleBoxFlat)_normalStyle.Duplicate();
		_activeStyle.BorderColor = new Color(0.3f, 0.6f, 1f, 1f);
		_activeStyle.BorderWidthLeft = _activeStyle.BorderWidthTop = _activeStyle.BorderWidthRight = _activeStyle.BorderWidthBottom = 4;

		_targetableStyle = (StyleBoxFlat)_normalStyle.Duplicate();
		_targetableStyle.BorderColor = new Color(1f, 0.85f, 0.2f, 1f);
		_targetableStyle.BorderWidthLeft = _targetableStyle.BorderWidthTop = _targetableStyle.BorderWidthRight = _targetableStyle.BorderWidthBottom = 4;

		CombatEventBus.AttackEvaded += OnAttackEvaded;
		CombatEventBus.EntityDamaged += OnEntityDamaged;
		CombatEventBus.EntityHealed += OnEntityHealed;
		CombatEventBus.EntityDeath += OnEntityDeath;
		CombatEventBus.EntityRevived += OnEntityRevived;
		CombatEventBus.KeywordApplied += OnKeywordApplied;
		CombatEventBus.BuffDebuffApplied += OnBuffDebuffApplied;
		CombatEventBus.BuffDebuffTicked += OnBuffDebuffTicked;
		CombatEventBus.BuffDebuffExpired += OnBuffDebuffExpired;
		CombatEventBus.RegenDrainApplied += OnRegenDrainApplied;
		CombatEventBus.RegenDrainTicked += OnRegenDrainTicked;
		CombatEventBus.RegenDrainExpired += OnRegenDrainExpired;
		CombatEventBus.PassiveApplied += OnPassiveApplied;
		CombatEventBus.PassiveRemoved += OnPassiveRemoved;
	}

	public override void _ExitTree()
	{
		CombatEventBus.AttackEvaded -= OnAttackEvaded;
		CombatEventBus.EntityDamaged -= OnEntityDamaged;
		CombatEventBus.EntityHealed -= OnEntityHealed;
		CombatEventBus.EntityDeath -= OnEntityDeath;
		CombatEventBus.EntityRevived -= OnEntityRevived;
		CombatEventBus.KeywordApplied -= OnKeywordApplied;
		CombatEventBus.BuffDebuffApplied -= OnBuffDebuffApplied;
		CombatEventBus.BuffDebuffTicked -= OnBuffDebuffTicked;
		CombatEventBus.BuffDebuffExpired -= OnBuffDebuffExpired;
		CombatEventBus.RegenDrainApplied -= OnRegenDrainApplied;
		CombatEventBus.RegenDrainTicked -= OnRegenDrainTicked;
		CombatEventBus.RegenDrainExpired -= OnRegenDrainExpired;
		CombatEventBus.PassiveApplied -= OnPassiveApplied;
		CombatEventBus.PassiveRemoved -= OnPassiveRemoved;
	}

	// PanelContainer defaults to MouseFilter.Stop and the inner Columns tree is set to Ignore in
	// the .tscn, so clicks on the card (but not its labels) reach here without any overlay node.
	public override void _GuiInput(InputEvent @event)
	{
		if (!_targetable) return;

		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			Clicked?.Invoke(_entityId);
	}

	// Blue outline while it's this entity's turn. Kept off Modulate, which the death-grey and
	// damage-flash tweens below already own.
	public void SetActiveTurn(bool active)
	{
		_isActiveTurn = active;
		RefreshCardStyle();
	}

	// Gold outline while this card is a valid click target for the pending action.
	public void SetTargetable(bool targetable)
	{
		_targetable = targetable;
		RefreshCardStyle();
	}

	private void RefreshCardStyle() =>
		AddThemeStyleboxOverride("panel", _targetable ? _targetableStyle : _isActiveTurn ? _activeStyle : _normalStyle);

	// Pushed by Battle after a KeywordApplied event moves this actor's Growth stack, so the
	// inventory list re-renders with the current bonus instead of only the floating "+N%" label.
	public void RefreshInventory(IReadOnlyDictionary<(string ActorId, string SourceId), double> growthBonuses)
	{
		_growthBonuses = growthBonuses;
		RenderInventory();
	}

	private void RenderInventory()
	{
		foreach (Node child in _inventoryContainer.GetChildren())
			child.QueueFree();

		foreach (var entry in _inventoryEntries)
			AddInventoryLabel(entry);
	}

	private void AddInventoryLabel(CombatantInventoryEntry entry)
	{
		var text = entry.Name;
		if (entry.Keywords.Contains(GrowthKeyword.KeywordName))
			text += $"  Growth +{_growthBonuses.GetValueOrDefault((_entityId, entry.EntityId)):P0}";

		var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.Word };
		_inventoryContainer.AddChild(label);
	}

	private void OnAttackEvaded(string attackerId, string attackerName, string targetId, string targetName, float oldEvasion, float newEvasion, string sourceId, string sourceName)
	{
		if (targetId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			_evadedLabel.Modulate = new Color(1, 1, 1, 1);
			_evadedLabel.Position = Vector2.Zero;

			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(_evadedLabel, "position:y", -20f, 0.8f);
			tween.TweenProperty(_evadedLabel, "modulate:a", 0f, 0.8f);
		});
	}

	private void OnEntityDamaged(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, bool isCriticalHit, int oldHp, int newHp)
	{
		if (targetId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
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
		});
	}

	private void OnEntityHealed(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, int oldHp, int newHp)
	{
		if (targetId != _entityId) return;
		// amount is the clamped delta (Hp - oldHp), so a heal on a full-HP target reports 0 - skip
		// the animation rather than floating a "+0".
		if (amount <= 0) return;

		UiEventQueue.Enqueue(() =>
		{
			_healLabel.Text = $"+{amount}";
			_healLabel.Modulate = new Color(1, 1, 1, 1);
			_healLabel.Position = Vector2.Zero;

			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(_healLabel, "position:y", -20f, 0.8f);
			tween.TweenProperty(_healLabel, "modulate:a", 0f, 0.8f);

			Modulate = new Color(0.3f, 1f, 0.3f, 1f);
			var flashTween = CreateTween();
			flashTween.TweenProperty(this, "modulate", new Color(1, 1, 1, 1), 0.25f);
		});
	}

	private void OnKeywordApplied(string keywordName, string actorId, string actorName, string targetId, string targetName, double bonus, string sourceId, string sourceName, int useCount)
	{
		if (targetId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			var label = new Label
			{
				Text = $"{keywordName} +{bonus:P0}",
				Modulate = new Color(0.4f, 0.75f, 1f, 1f),
				Position = Vector2.Zero,
				MouseFilter = MouseFilterEnum.Ignore,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Top,
			};
			AddChild(label);

			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(label, "position:y", -20f, 0.8f);
			tween.TweenProperty(label, "modulate:a", 0f, 0.8f);
			tween.Finished += label.QueueFree;
		});
	}

	private void OnEntityDeath(string entityId, string entityName, string sourceId, string sourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() => Modulate = new Color(0.4f, 0.4f, 0.4f, 1f));
	}

	private void OnEntityRevived(string entityId, string entityName, int oldHp, int newHp, string sourceId, string sourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() => Modulate = new Color(1, 1, 1, 1));
	}

	private void OnBuffDebuffApplied(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, int oldValue, int newValue, string sourceId, string sourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			_buffs[stat] = (isPositive, roundsRemaining, untilRemoved, newValue, sourceName);
			RenderStat(stat);
			RenderEffects();
		});
	}

	private void OnBuffDebuffTicked(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			if (!_buffs.TryGetValue(stat, out var existing)) return;
			_buffs[stat] = existing with { Rounds = roundsRemaining, SourceName = sourceName };
			RenderEffects();
		});
	}

	private void OnBuffDebuffExpired(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int oldValue, int newValue, string sourceId, string sourceName, string counteredBySourceId, string counteredBySourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			_buffs.Remove(stat);
			RenderStat(stat);
			RenderEffects();
		});
	}

	private void OnRegenDrainApplied(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, string sourceId, string sourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			_regens[stat] = (isPositive, roundsRemaining, untilRemoved, sourceName);
			RenderEffects();
		});
	}

	private void OnRegenDrainTicked(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			if (!_regens.TryGetValue(stat, out var existing)) return;
			_regens[stat] = existing with { Rounds = roundsRemaining, SourceName = sourceName };
			RenderEffects();
		});
	}

	private void OnRegenDrainExpired(string entityId, string entityName, RegenDrainStat stat, bool isPositive, string sourceId, string sourceName, string counteredBySourceId, string counteredBySourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			_regens.Remove(stat);
			RenderEffects();
		});
	}

	private void OnPassiveApplied(string entityId, string entityName, string passiveName, string sourceId, string sourceName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			_passives.Add(passiveName);
			RenderEffects();
		});
	}

	private void OnPassiveRemoved(string entityId, string entityName, string passiveName)
	{
		if (entityId != _entityId) return;

		UiEventQueue.Enqueue(() =>
		{
			_passives.Remove(passiveName);
			RenderEffects();
		});
	}

	private void RenderStat(BuffDebuffStat stat)
	{
		var label = stat switch
		{
			BuffDebuffStat.Power   => _powerEffLabel,
			BuffDebuffStat.Defense => _defenseEffLabel,
			BuffDebuffStat.Speed   => _speedEffLabel,
			_ => null,
		};
		if (label is null) return;

		if (_buffs.TryGetValue(stat, out var buff))
		{
			label.Text = $"({buff.Value})";
			label.AddThemeColorOverride("font_color", buff.IsPositive ? PositiveColor : NegativeColor);
		}
		else
		{
			label.Text = "";
		}
	}

	private void RenderEffects()
	{
		foreach (Node child in _effectsContainer.GetChildren())
			child.QueueFree();

		foreach (var (stat, e) in _buffs)
		{
			var label = new Label
			{
				Text = $"{StatAbbrev(stat)} {(e.IsPositive ? "↑" : "↓")} {Duration(e.Rounds, e.UntilRemoved)} ({e.SourceName})",
			};
			label.AddThemeColorOverride("font_color", e.IsPositive ? PositiveColor : NegativeColor);
			_effectsContainer.AddChild(label);
		}

		foreach (var (stat, e) in _regens)
		{
			var label = new Label
			{
				Text = $"{ResourceAbbrev(stat)} {(e.IsPositive ? "regen" : "drain")} {Duration(e.Rounds, e.UntilRemoved)} ({e.SourceName})",
			};
			label.AddThemeColorOverride("font_color", e.IsPositive ? PositiveColor : NegativeColor);
			_effectsContainer.AddChild(label);
		}

		foreach (var passiveName in _passives)
		{
			var label = new Label { Text = $"◆ {passiveName}" };
			label.AddThemeColorOverride("font_color", PassiveColor);
			_effectsContainer.AddChild(label);
		}
	}

	private static string Duration(int rounds, bool untilRemoved) => untilRemoved ? "∞" : $"{rounds}t";

	private static string StatAbbrev(BuffDebuffStat stat) => stat switch
	{
		BuffDebuffStat.Power   => "PWR",
		BuffDebuffStat.Defense => "DEF",
		BuffDebuffStat.Speed   => "SPD",
		_ => stat.ToString(),
	};

	private static string ResourceAbbrev(RegenDrainStat stat) => stat switch
	{
		RegenDrainStat.Hp => "HP",
		RegenDrainStat.Tp => "TP",
		_ => stat.ToString(),
	};
}
