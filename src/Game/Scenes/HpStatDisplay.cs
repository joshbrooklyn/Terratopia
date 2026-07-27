using CombatEngine;
using Godot;

public partial class HpStatDisplay : Label
{
	private string _entityId = "";
	private int _currentHp;
	private int _maxHp;

	public void Initialize(string entityId, int currentHp, int maxHp)
	{
		_entityId = entityId;
		_currentHp = currentHp;
		_maxHp = maxHp;
		Text = $"HP: {currentHp} / {maxHp}";
		CombatEventBus.EntityHpChanged += OnEntityHpChanged;
		CombatEventBus.EntityMaxHpChanged += OnEntityMaxHpChanged;
	}

	public override void _ExitTree()
	{
		CombatEventBus.EntityHpChanged -= OnEntityHpChanged;
		CombatEventBus.EntityMaxHpChanged -= OnEntityMaxHpChanged;
	}

	private void OnEntityHpChanged(string entityId, string entityName, int oldHp, int newHp)
	{
		if (entityId != _entityId) return;
		_currentHp = newHp;
		Text = $"HP: {_currentHp} / {_maxHp}";
	}

	private void OnEntityMaxHpChanged(string entityId, string entityName, int oldMaxHp, int newMaxHp)
	{
		if (entityId != _entityId) return;
		_maxHp = newMaxHp;
		Text = $"HP: {_currentHp} / {_maxHp}";
	}
}
