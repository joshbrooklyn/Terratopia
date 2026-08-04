using CombatEngine.DataClasses;

namespace CombatEngine.Engine;

public class TurnOrderManager
{
    private readonly Random _rng;
    private Queue<CombatEntity> _queue = new();
    public bool IsRoundOver => _queue.Count == 0;
    public IReadOnlyList<string> CurrentTurnOrderIds => _queue.Select(e => e.EntityId).ToList();
    public IReadOnlyList<string> CurrentTurnOrderNames => _queue.Select(e => e.Name).ToList();

    public TurnOrderManager(Random? rng = null)
    {
        _rng = rng ?? new Random();
    }

    public void BuildRound(IReadOnlyList<CombatEntity> entities)
    {
        var turnOrder = CombatBalance.Current.TurnOrder;
        var baseMultiplier = (float)turnOrder.BaseMultiplier;
        var jitterRange     = (float)turnOrder.JitterRange;
        var jitterOffset    = (float)turnOrder.JitterOffset;

        var scored = entities
            .Select(e => (entity: e, score: e.Speed * (baseMultiplier + (_rng.NextSingle() * jitterRange - jitterOffset))))
            .OrderByDescending(x => x.score)
            .Select(x => x.entity);
        _queue = new Queue<CombatEntity>(scored);
    }

    public CombatEntity? NextTurn()
    {
        return _queue.Count > 0 ? _queue.Dequeue() : null;
    }
}
