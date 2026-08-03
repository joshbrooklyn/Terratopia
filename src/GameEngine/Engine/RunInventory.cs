using GameEngine.DataClasses;

namespace GameEngine.Engine;

// Mutable per-run remaining-uses store for items. Seeded once at run start from each selected
// adventurer's ItemIds and the item's MaxUses, then decremented per use. Unlike CombatEngine
// state, which is rebuilt outright by every InitCombat, this survives across the whole run.
internal sealed class RunInventory
{
    private readonly Dictionary<(string AdventurerId, string ItemId), int> _remaining = new();

    // Refills every held item to its MaxUses, discarding whatever the previous run had left.
    internal void StartRun(IEnumerable<Adventurer> party, Func<string, Item> lookupItem)
    {
        _remaining.Clear();

        foreach (var adventurer in party)
            foreach (var itemId in adventurer.ItemIds)
                _remaining[(adventurer.AdventurerId, itemId)] = lookupItem(itemId).MaxUses;
    }

    // Zero for an item the adventurer doesn't hold, so callers can treat "unheld" and "exhausted"
    // identically - both mean the item is unusable.
    internal int GetRemaining(string adventurerId, string itemId) =>
        _remaining.GetValueOrDefault((adventurerId, itemId));

    // False when the item is exhausted or was never held, leaving the count untouched.
    internal bool TryConsume(string adventurerId, string itemId)
    {
        var key = (adventurerId, itemId);
        if (_remaining.GetValueOrDefault(key) <= 0)
            return false;

        _remaining[key]--;
        return true;
    }
}
