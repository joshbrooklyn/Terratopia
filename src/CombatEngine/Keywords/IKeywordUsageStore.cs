namespace CombatEngine.Keywords;

// A named-counter store for stacking keywords (e.g. Growth, Teamwork) to track their own usage.
// Each keyword defines its own key format/scope (see individual keyword classes). Implemented by
// CombatEngineClass, so counts are scoped to a single combat encounter.
public interface IKeywordUsageStore
{
    int Increment(string key);
    int GetCount(string key);
}
