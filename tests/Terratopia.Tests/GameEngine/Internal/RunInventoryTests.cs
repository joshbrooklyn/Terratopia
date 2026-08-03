using CombatEngine.Enums;
using GameEngine.DataClasses;
using GameEngine.Engine;

namespace Terratopia.Tests.GameEngine.Internal;

public class RunInventoryTests
{
    // Minimal Item/Adventurer builders so RunInventory can be tested without ContentLoader,
    // which would require the TerratopiaGameDataPath environment variable and real JSON on disk.
    private static Item MakeItem(string itemId, int maxUses) => new()
    {
        SchemaVersion  = 4,
        ItemId         = itemId,
        Name           = itemId,
        MaxUses        = maxUses,
        ValidTargets   = ValidTarget.Enemies,
        LivingOrDead   = LivingOrDead.Living,
        CombatFunction = "NoOp",
    };

    private static Adventurer MakeAdventurer(string adventurerId, params string[] itemIds) => new()
    {
        SchemaVersion = 1,
        AdventurerId  = adventurerId,
        Name          = adventurerId,
        ItemIds       = [.. itemIds],
    };

    // Stands in for GameEngineClass's `_allItems.Lookup` delegate.
    private static Func<string, Item> LookupFrom(params Item[] items) =>
        id => items.First(i => i.ItemId == id);

    [Fact]
    public void StartRun_SeedsEachItemToItsMaxUses()
    {
        // What: verifies a freshly started run gives every item an adventurer holds exactly the
        //       number of uses declared on the Item, rather than a shared or hardcoded value.
        // How:  the party holds two items with deliberately different MaxUses - "scroll" at 3 and
        //       "potion" at 1 - so a bug that seeded a single constant for all items would fail
        //       one of the two assertions. GetRemaining is read immediately after StartRun with
        //       no TryConsume in between, so both must still be at their declared maximums.
        var scroll = MakeItem("scroll", maxUses: 3);
        var potion = MakeItem("potion", maxUses: 1);
        var laura  = MakeAdventurer("laura", "scroll", "potion");
        var inv    = new RunInventory();

        inv.StartRun([laura], LookupFrom(scroll, potion));

        Assert.Equal(3, inv.GetRemaining("laura", "scroll"));
        Assert.Equal(1, inv.GetRemaining("laura", "potion"));
    }

    [Fact]
    public void TryConsume_DecrementsRemainingByOne()
    {
        // What: verifies a successful use spends exactly one charge and reports success, so a
        //       3-use item reads 2 after one use rather than dropping straight to 0 or staying 3.
        // How:  "scroll" is seeded at MaxUses=3. One TryConsume must return true (the item was
        //       available) and leave GetRemaining at 3-1=2. A second TryConsume drops it to 1,
        //       confirming the decrement is per-call and not a one-shot flag flip.
        var scroll = MakeItem("scroll", maxUses: 3);
        var inv    = new RunInventory();
        inv.StartRun([MakeAdventurer("laura", "scroll")], LookupFrom(scroll));

        Assert.True(inv.TryConsume("laura", "scroll"));
        Assert.Equal(2, inv.GetRemaining("laura", "scroll"));

        Assert.True(inv.TryConsume("laura", "scroll"));
        Assert.Equal(1, inv.GetRemaining("laura", "scroll"));
    }

    [Fact]
    public void TryConsume_AtZeroRemaining_ReturnsFalseAndDoesNotGoNegative()
    {
        // What: verifies an exhausted item refuses further uses and its count floors at 0, so a
        //       caller that ignores the return value can't drive the remaining count negative.
        // How:  "potion" is seeded at MaxUses=1. The first TryConsume succeeds and takes it to 0.
        //       Two further TryConsume calls must both return false, and GetRemaining must still
        //       read 0 rather than -1 or -2 - the guard returns before touching the dictionary.
        var potion = MakeItem("potion", maxUses: 1);
        var inv    = new RunInventory();
        inv.StartRun([MakeAdventurer("laura", "potion")], LookupFrom(potion));

        Assert.True(inv.TryConsume("laura", "potion"));
        Assert.Equal(0, inv.GetRemaining("laura", "potion"));

        Assert.False(inv.TryConsume("laura", "potion"));
        Assert.False(inv.TryConsume("laura", "potion"));
        Assert.Equal(0, inv.GetRemaining("laura", "potion"));
    }

    [Fact]
    public void GetRemaining_ForItemTheAdventurerDoesNotHold_ReturnsZero()
    {
        // What: verifies an unheld item reads as 0 rather than throwing, so the UI can treat
        //       "never held" and "exhausted" identically - both mean the button is disabled.
        // How:  laura's ItemIds contains only "scroll", so the (laura, potion) key was never
        //       seeded even though "potion" exists as an Item. GetRemaining must fall back to
        //       the dictionary default of 0, and TryConsume must refuse rather than creating
        //       the missing key. An unknown adventurer id is checked for the same reason.
        var scroll = MakeItem("scroll", maxUses: 3);
        var potion = MakeItem("potion", maxUses: 1);
        var inv    = new RunInventory();
        inv.StartRun([MakeAdventurer("laura", "scroll")], LookupFrom(scroll, potion));

        Assert.Equal(0, inv.GetRemaining("laura", "potion"));
        Assert.Equal(0, inv.GetRemaining("nobody", "scroll"));
        Assert.False(inv.TryConsume("laura", "potion"));
    }

    [Fact]
    public void TryConsume_IsScopedPerAdventurer()
    {
        // What: verifies two party members carrying the same item id keep independent counts, so
        //       one adventurer burning their copy doesn't spend anyone else's.
        // How:  both laura and gareth hold "scroll" (MaxUses=3), giving two distinct dictionary
        //       keys. Only laura consumes, twice, so her count reads 3-2=1 while gareth's stays
        //       untouched at 3. A store keyed on itemId alone would report 1 for both.
        var scroll = MakeItem("scroll", maxUses: 3);
        var inv    = new RunInventory();
        inv.StartRun(
            [MakeAdventurer("laura", "scroll"), MakeAdventurer("gareth", "scroll")],
            LookupFrom(scroll));

        inv.TryConsume("laura", "scroll");
        inv.TryConsume("laura", "scroll");

        Assert.Equal(1, inv.GetRemaining("laura", "scroll"));
        Assert.Equal(3, inv.GetRemaining("gareth", "scroll"));
    }

    [Fact]
    public void StartRun_CalledAgain_RefillsPreviouslyDepletedItems()
    {
        // What: verifies starting a new run wipes the previous run's spending, which is what
        //       makes returning to the main menu and starting a fresh skirmish hand back full
        //       items rather than the leftovers from last time.
        // How:  "scroll" (MaxUses=3) is fully drained by three TryConsume calls, leaving 0. A
        //       second StartRun with the same party must reset it to 3. The second run also
        //       drops gareth from the party, so his stale key must be gone (0) - proving
        //       StartRun clears the dictionary rather than only overwriting the keys it reseeds.
        var scroll = MakeItem("scroll", maxUses: 3);
        var laura  = MakeAdventurer("laura", "scroll");
        var gareth = MakeAdventurer("gareth", "scroll");
        var inv    = new RunInventory();

        inv.StartRun([laura, gareth], LookupFrom(scroll));
        inv.TryConsume("laura", "scroll");
        inv.TryConsume("laura", "scroll");
        inv.TryConsume("laura", "scroll");
        Assert.Equal(0, inv.GetRemaining("laura", "scroll"));

        inv.StartRun([laura], LookupFrom(scroll));

        Assert.Equal(3, inv.GetRemaining("laura", "scroll"));
        Assert.Equal(0, inv.GetRemaining("gareth", "scroll"));
    }
}
