using CombatEngine.DataClasses;
using CombatEngine.Keywords;

namespace Terratopia.Tests.CombatEngine;

[Collection("CombatEngineSerial")]
public class TeamworkKeywordTests
{
    // Minimal in-memory IKeywordUsageStore, mirroring how CombatEngineClass implements it,
    // so TeamworkKeyword can be unit-tested without spinning up a full combat.
    private sealed class FakeUsageStore : IKeywordUsageStore
    {
        private readonly Dictionary<string, int> _counts = new();
        public int Increment(string key) => _counts[key] = _counts.GetValueOrDefault(key) + 1;
        public int GetCount(string key) => _counts.GetValueOrDefault(key);
    }

    private static CombatEntity MakeEntity(string id) => new(
        entityId: id, name: id, level: 1,
        maxHp: 10, hp: 10, maxTp: 0, tp: 0,
        power: 10, defense: 0, speed: 0,
        evasion: 0f, critChance: 0f, critModifier: 0f);

    [Fact]
    public void OnUsed_IncrementsOnlyTheActorsSide()
    {
        // What: verifies OnUsed increments the ally-side counter when the actor is an ally, and
        //       leaves the enemy-side counter untouched (and vice versa).
        // How:  Two separate FakeUsageStore instances isolate ally vs enemy scenarios. Calling
        //       OnUsed once with actorIsAlly=true should make GetBonus (which reads the same
        //       side's count) report 0.05, while the enemy-side key stays at its default 0 count.
        var store = new FakeUsageStore();
        var keyword = new TeamworkKeyword();
        var actor = MakeEntity("ally1");
        var target = MakeEntity("enemy1");

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "", store);

        Assert.Equal(0.05, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "", store));
        Assert.Equal(0.0, keyword.GetBonus(actor, target, actorIsAlly: false, actionId: "", store));
    }

    [Fact]
    public void GetBonus_ScalesWithFivePercentPerStack()
    {
        // What: verifies each OnUsed call adds exactly 5% to the bonus reported for that side,
        //       reproducing "first use +5%, second use +10%" directly against the keyword class.
        var store = new FakeUsageStore();
        var keyword = new TeamworkKeyword();
        var actor = MakeEntity("ally1");
        var target = MakeEntity("enemy1");

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "", store);
        Assert.Equal(0.05, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "", store), precision: 10);

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "", store);
        Assert.Equal(0.10, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "", store), precision: 10);

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "", store);
        Assert.Equal(0.15, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "", store), precision: 10);
    }

    [Fact]
    public void PowerKeywordRegistry_ResolvesTeamworkByName()
    {
        // What: verifies the registry maps the literal "Teamwork" string to a TeamworkKeyword
        //       instance, and silently ignores unrecognized keyword strings.
        var resolved = PowerKeywordRegistry.Resolve(["Teamwork", "SomeUnknownKeyword"]).ToList();

        Assert.Single(resolved);
        Assert.IsType<TeamworkKeyword>(resolved[0]);
    }

    [Fact]
    public void PowerKeywordRegistry_CollapsesDuplicateNames()
    {
        // What: verifies "adding a keyword twice has no effect" at the registry level — a
        //       command listing "Teamwork" twice should still only resolve to one keyword
        //       instance, so OnUsed/GetBonus aren't applied twice for the same action.
        var resolved = PowerKeywordRegistry.Resolve(["Teamwork", "Teamwork"]).ToList();

        Assert.Single(resolved);
    }
}
