using CombatEngine.DataClasses;
using CombatEngine.Keywords;

namespace Terratopia.Tests.CombatEngine.Internal;

[Collection("CombatEngineSerial")]
public class GrowthKeywordTests
{
    // Minimal in-memory IKeywordUsageStore, mirroring how CombatEngineClass implements it,
    // so GrowthKeyword can be unit-tested without spinning up a full combat.
    private sealed class FakeUsageStore : IKeywordUsageStore
    {
        private readonly Dictionary<string, int> _counts = new();
        public int Increment(string key) => _counts[key] = _counts.GetValueOrDefault(key) + 1;
        public int GetCount(string key) => _counts.GetValueOrDefault(key);
    }

    private static CombatEntity MakeEntity(string id) => new(
        entityId: id, name: id, level: 1,
        maxHp: 100, hp: 100, maxTp: 0, tp: 0,
        power: 10, defense: 0, speed: 0,
        evasion: 0f, critChance: 0f, critModifier: 0f);

    [Fact]
    public void FirstUse_GrantsNoBonus()
    {
        // What: verifies "after each use, increase the power modifier by 10%" means the
        //       triggering use itself gets no bonus - growth only benefits uses after it.
        // How:  GrowthKeyword.GetBonus computes 0.10 * max(0, count-1), keyed by
        //       "growth:{actorId}:{actionId}". OnUsed increments the counter to 1 for this
        //       first use, so GetBonus reads count=1, giving 0.10*max(0,0)=0. The test asserts
        //       GetBonus returns exactly 0.0 immediately after the first OnUsed call.
        var store = new FakeUsageStore();
        var keyword = new GrowthKeyword();
        var actor = MakeEntity("actor");
        var target = MakeEntity("target");

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "tech1", store);

        Assert.Equal(0.0, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "tech1", store), precision: 10);
    }

    [Fact]
    public void SubsequentUses_StackByTenPercentPerPriorUse()
    {
        // What: verifies each additional use of the same action by the same actor increases
        //       the reported bonus by a further 10%, i.e. the bonus grows linearly with the
        //       number of *prior* uses rather than staying flat or compounding.
        // How:  Each OnUsed call increments the "growth:actor:tech1" counter by one before
        //       GetBonus is checked, so after 1/2/3 uses the counter reads 1/2/3 and
        //       GetBonus = 0.10*max(0,count-1) evaluates to 0.10*0=0.00, 0.10*1=0.10, and
        //       0.10*2=0.20 respectively. The test asserts the bonus after each successive
        //       OnUsed call matches that sequence.
        var store = new FakeUsageStore();
        var keyword = new GrowthKeyword();
        var actor = MakeEntity("actor");
        var target = MakeEntity("target");

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "tech1", store);
        Assert.Equal(0.00, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "tech1", store), precision: 10);

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "tech1", store);
        Assert.Equal(0.10, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "tech1", store), precision: 10);

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "tech1", store);
        Assert.Equal(0.20, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "tech1", store), precision: 10);
    }

    [Fact]
    public void DifferentActionId_TracksItsOwnCounter()
    {
        // What: verifies Growth's usage counter is scoped per action id, not shared globally
        //       across everything an actor does - using "tech1" twice must not leak any bonus
        //       onto an unrelated "tech2" that has never been used.
        // How:  The usage key is "growth:{actorId}:{actionId}", so two OnUsed calls for
        //       actionId="tech1" only bump the "growth:actor:tech1" entry, leaving
        //       "growth:actor:tech2" at its default count of 0. GetBonus for tech1 (count=2)
        //       is 0.10*max(0,2-1)=0.10, while tech2 (count=0) is 0.10*max(0,0-1)=0.00 (the
        //       Math.Max floor keeps this from going negative for a never-used action).
        var store = new FakeUsageStore();
        var keyword = new GrowthKeyword();
        var actor = MakeEntity("actor");
        var target = MakeEntity("target");

        keyword.OnUsed(actor, actorIsAlly: true, actionId: "tech1", store);
        keyword.OnUsed(actor, actorIsAlly: true, actionId: "tech1", store);

        // tech1 is on its second use (bonus 0.10), but tech2 hasn't been used yet.
        Assert.Equal(0.10, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "tech1", store), precision: 10);
        Assert.Equal(0.00, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "tech2", store), precision: 10);
    }

    [Fact]
    public void DifferentActor_TracksItsOwnCounterForTheSameActionId()
    {
        // What: verifies Growth's usage counter is also scoped per actor - two different
        //       actors independently spamming the same action id ("tech1") must not share a
        //       counter, since the usage key embeds both the actor id and the action id.
        // How:  Only actor1 calls OnUsed (twice) for "tech1", bumping "growth:actor1:tech1" to
        //       count=2 and its bonus to 0.10*max(0,2-1)=0.10. actor2 has never used "tech1" at
        //       all, so its key "growth:actor2:tech1" is still at the default count of 0,
        //       giving GetBonus for actor2 = 0.10*max(0,0-1)=0.00, even though the action id
        //       string is identical.
        var store = new FakeUsageStore();
        var keyword = new GrowthKeyword();
        var actor1 = MakeEntity("actor1");
        var actor2 = MakeEntity("actor2");
        var target = MakeEntity("target");

        keyword.OnUsed(actor1, actorIsAlly: true, actionId: "tech1", store);
        keyword.OnUsed(actor1, actorIsAlly: true, actionId: "tech1", store);

        Assert.Equal(0.10, keyword.GetBonus(actor1, target, actorIsAlly: true, actionId: "tech1", store), precision: 10);
        Assert.Equal(0.00, keyword.GetBonus(actor2, target, actorIsAlly: true, actionId: "tech1", store), precision: 10);
    }

    [Fact]
    public void PowerKeywordRegistry_ResolvesGrowthByName()
    {
        // What: verifies the registry maps the literal "Growth" string to a GrowthKeyword
        //       instance, mirroring PowerKeywordRegistry_ResolvesTeamworkByName in
        //       TeamworkKeywordTests.
        var resolved = PowerKeywordRegistry.Resolve(["Growth"]).ToList();

        Assert.Single(resolved);
        Assert.IsType<GrowthKeyword>(resolved[0]);
    }
}
