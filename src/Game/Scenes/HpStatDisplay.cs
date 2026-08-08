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
		CombatEventBus.EntityDamaged += OnEntityDamaged;
		CombatEventBus.EntityHealed += OnEntityHealed;
		CombatEventBus.EntityRevived += OnEntityRevived;
	}

	public override void _ExitTree()
	{
		CombatEventBus.EntityDamaged -= OnEntityDamaged;
		CombatEventBus.EntityHealed -= OnEntityHealed;
		CombatEventBus.EntityRevived -= OnEntityRevived;
	}

	private void OnEntityDamaged(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, bool isCriticalHit, int oldHp, int newHp)
	{
		if (targetId != _entityId) return;
		UiEventQueue.Enqueue(() => SetHp(newHp));
	}

	private void OnEntityHealed(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, int oldHp, int newHp)
	{
		if (targetId != _entityId) return;
		UiEventQueue.Enqueue(() => SetHp(newHp));
	}

	private void OnEntityRevived(string entityId, string entityName, int oldHp, int newHp, string sourceId, string sourceName)
	{
		if (entityId != _entityId) return;
		UiEventQueue.Enqueue(() => SetHp(newHp));
	}

	private void SetHp(int newHp)
	{
		_currentHp = newHp;
		Text = $"HP: {_currentHp} / {_maxHp}";
	}
}
