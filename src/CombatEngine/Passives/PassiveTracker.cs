namespace CombatEngine.Passives;

// One record per (passive, entity) pair for the current combat.
public struct PassiveActivation
{
    public Passive Passive;               // the registry singleton, resolved once by Add
    public string  EntityId;
    public int     RoundApplied;          // round the entity acquired this passive
    public int     ApplicationsThisRound; // zeroed by BeginRound
    public int     TotalApplications;     // for the whole combat
}

// The authority on which passives an entity owns and how often they've fired. Scoped to a single
// combat: CombatEngineClass.InitCombat resets it, so passives must be granted (via Add) after
// InitCombat runs, not before - anything added earlier would just be wiped by Reset.
public static class PassiveTracker
{
    private static readonly Dictionary<(string Passive, string EntityId), PassiveActivation> _activations = new();
    private static int _currentRound = 1;

    public static int CurrentRound => _currentRound;

    // Called by CombatEngineClass.InitCombat. _currentRound starts at 1 - combat's first round is
    // 1, and passives are granted before BuildRound has run - so a passive granted at setup reads
    // as 0 rounds elapsed during round 1, same as one granted mid-combat during round 1.
    public static void Reset()
    {
        _activations.Clear();
        _currentRound = 1;
    }

    // Called by CombatEngineClass.BuildRound. Per-round counts are cleared here; totals are not.
    internal static void BeginRound(int round)
    {
        _currentRound = round;
        foreach (var key in _activations.Keys.ToList())
        {
            var activation = _activations[key];
            activation.ApplicationsThisRound = 0;
            _activations[key] = activation;
        }
    }

    // Grants a passive to an entity, stamping the current round. This is how an entity comes to
    // own a passive at all - at combat setup or mid-combat, same call. Adding a passive the entity
    // already owns is a no-op, so the original RoundApplied and counts stand. Unrecognised names
    // resolve to null and are silently dropped, the same way PassiveRegistry.Resolve has always
    // treated bad names - no record is created. Returns true only when a new record was actually
    // created, so a mid-combat caller (CombatFunction.ApplyPassives) can tell a genuine grant apart
    // from a no-op and raise CombatEventBus.PassiveApplied only for the former.
    public static bool Add(string passiveName, string entityId)
    {
        var key = (passiveName, entityId);
        if (_activations.ContainsKey(key))
            return false;

        var passive = PassiveRegistry.Resolve(passiveName);
        if (passive == null)
            return false;

        _activations[key] = new PassiveActivation
        {
            Passive               = passive,
            EntityId              = entityId,
            RoundApplied          = _currentRound,
            ApplicationsThisRound = 0,
            TotalApplications     = 0,
        };
        return true;
    }

    // Strips a passive from an entity mid-combat. Drops the record outright, so the entity's
    // activation history for it goes too: a later Add() re-stamps RoundApplied to that round and
    // restarts the counts from zero, which also re-arms a once-per-combat passive like LivingDead.
    // Removing a passive the entity doesn't own is a no-op.
    public static void Remove(string passiveName, string entityId) =>
        _activations.Remove((passiveName, entityId));

    // The passives an entity owns, as live instances. Backs HandleDefeat's dispatch loop, which
    // therefore does no registry lookup at all. Order is unspecified.
    public static IEnumerable<Passive> GetPassives(string entityId) =>
        _activations.Where(kv => kv.Key.EntityId == entityId).Select(kv => kv.Value.Passive);

    // Called by a passive itself, at the point it decides it actually did something. Takes the
    // instance rather than a name so the lazy-create path below - for a passive invoked outside a
    // running combat, as the unit tests do - has something to store without a registry lookup.
    public static void RecordActivation(Passive passive, string entityId)
    {
        var key = (passive.Name, entityId);
        if (!_activations.TryGetValue(key, out var activation))
        {
            activation = new PassiveActivation
            {
                Passive      = passive,
                EntityId     = entityId,
                RoundApplied = _currentRound,
            };
        }

        activation.ApplicationsThisRound++;
        activation.TotalApplications++;
        _activations[key] = activation;
    }

    // Returns a default-valued struct for an untracked pair, so callers can read
    // TotalApplications == 0 without a TryGet dance.
    public static PassiveActivation Get(string passiveName, string entityId) =>
        _activations.GetValueOrDefault((passiveName, entityId));
}
