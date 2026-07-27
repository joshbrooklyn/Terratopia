using CombatEngine;
using Godot;

public partial class TpStatDisplay : Label
{
	private string _entityId = "";
	private int _currentTp;
	private int _maxTp;

	public void Initialize(string entityId, int currentTp, int maxTp)
	{
		_entityId = entityId;
		_currentTp = currentTp;
		_maxTp = maxTp;
		Text = $"TP: {currentTp} / {maxTp}";
		CombatEventBus.EntityTpChanged += OnEntityTpChanged;
		CombatEventBus.EntityMaxTpChanged += OnEntityMaxTpChanged;
	}

	public override void _ExitTree()
	{
		CombatEventBus.EntityTpChanged -= OnEntityTpChanged;
		CombatEventBus.EntityMaxTpChanged -= OnEntityMaxTpChanged;
	}

	private void OnEntityTpChanged(string entityId, string entityName, int oldTp, int newTp)
	{
		if (entityId != _entityId) return;
		_currentTp = newTp;
		Text = $"TP: {_currentTp} / {_maxTp}";
	}

	private void OnEntityMaxTpChanged(string entityId, string entityName, int oldMaxTp, int newMaxTp)
	{
		if (entityId != _entityId) return;
		_maxTp = newMaxTp;
		Text = $"TP: {_currentTp} / {_maxTp}";
	}
}
