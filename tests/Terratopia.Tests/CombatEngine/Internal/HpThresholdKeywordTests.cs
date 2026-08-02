using CombatEngine.DataClasses;
using CombatEngine.Keywords;

namespace Terratopia.Tests.CombatEngine.Internal;

[Collection("CombatEngineSerial")]
public class HpThresholdKeywordTests
{
    // Minimal in-memory IKeywordUsageStore; these keywords never touch it, but the interface
    // requires one.
    private sealed class FakeUsageStore : IKeywordUsageStore
    {
        private readonly Dictionary<string, int> _counts = new();
        public int Increment(string key) => _counts[key] = _counts.GetValueOrDefault(key) + 1;
        public int GetCount(string key) => _counts.GetValueOrDefault(key);
    }

    private static CombatEntity MakeEntity(string id, int hp, int maxHp) => new(
        entityId: id, name: id, level: 1,
        maxHp: maxHp, hp: hp, maxTp: 0, tp: 0,
        power: 10, defense: 0, speed: 0,
        evasion: 0f, critChance: 0f, critModifier: 0f);

    private static readonly FakeUsageStore Store = new();

    [Theory]
    [InlineData(75, 0.5)]  // exactly at the >=75% threshold
    [InlineData(100, 0.5)] // full health
    [InlineData(74, 0.0)]  // just below the threshold
    [InlineData(0, 0.0)]
    public void Engage_ChecksTargetHpAgainstUpperThreshold(int targetHp, double expectedBonus)
    {
        // What: verifies EngageKeyword grants its flat +50% bonus only when the *target* is at
        //       or above 75% of its max HP, with the boundary itself counting as met (>=, not >).
        // How:  EngageKeyword.GetBonus is `target.Hp >= 0.75 * target.MaxHp ? 0.5 : 0.0` against
        //       a target with maxHp=100. The InlineData rows probe exactly-at-threshold (75,
        //       bonus), comfortably-above (100, bonus), just-below (74, no bonus), and the
        //       opposite extreme (0, no bonus), pinning down both the >= boundary and the two
        //       branches of the ternary.
        var keyword = new EngageKeyword();
        var actor = MakeEntity("actor", hp: 100, maxHp: 100);
        var target = MakeEntity("target", hp: targetHp, maxHp: 100);

        Assert.Equal(expectedBonus, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "", Store));
    }

    [Theory]
    [InlineData(25, 0.5)]  // exactly at the <=25% threshold
    [InlineData(0, 0.5)]
    [InlineData(26, 0.0)]  // just above the threshold
    [InlineData(100, 0.0)]
    public void Cruel_ChecksTargetHpAgainstLowerThreshold(int targetHp, double expectedBonus)
    {
        // What: verifies CruelKeyword grants its flat +50% bonus only when the *target* is at
        //       or below 25% of its max HP - the mirror-image threshold to Engage, favoring
        //       finishing off already-weak targets rather than healthy ones.
        // How:  CruelKeyword.GetBonus is `target.Hp <= 0.25 * target.MaxHp ? 0.5 : 0.0` against
        //       a target with maxHp=100. The rows probe exactly-at-threshold (25, bonus),
        //       the opposite extreme (0, bonus), just-above-the-threshold (26, no bonus), and
        //       full health (100, no bonus), pinning down the <= boundary and both branches.
        var keyword = new CruelKeyword();
        var actor = MakeEntity("actor", hp: 100, maxHp: 100);
        var target = MakeEntity("target", hp: targetHp, maxHp: 100);

        Assert.Equal(expectedBonus, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "", Store));
    }

    [Theory]
    [InlineData(75, 0.5)]
    [InlineData(100, 0.5)]
    [InlineData(74, 0.0)]
    [InlineData(0, 0.0)]
    public void Empowered_ChecksActorHpAgainstUpperThreshold(int actorHp, double expectedBonus)
    {
        // What: verifies EmpoweredKeyword grants its flat +50% bonus based on the *actor's* own
        //       HP being at or above 75% of max, not the target's - rewarding an actor who is
        //       still healthy, mirroring Engage's threshold but checked against the attacker.
        // How:  EmpoweredKeyword.GetBonus is `actor.Hp >= 0.75 * actor.MaxHp ? 0.5 : 0.0` against
        //       an actor with maxHp=100 (the target's HP is fixed at full and irrelevant here).
        //       The rows mirror the Engage test's boundary probes but applied to actorHp instead
        //       of targetHp: exactly-at-threshold (75, bonus), full health (100, bonus),
        //       just-below (74, no bonus), and empty (0, no bonus).
        var keyword = new EmpoweredKeyword();
        var actor = MakeEntity("actor", hp: actorHp, maxHp: 100);
        var target = MakeEntity("target", hp: 100, maxHp: 100);

        Assert.Equal(expectedBonus, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "", Store));
    }

    [Theory]
    [InlineData(25, 0.5)]
    [InlineData(0, 0.5)]
    [InlineData(26, 0.0)]
    [InlineData(100, 0.0)]
    public void Stoic_ChecksActorHpAgainstLowerThreshold(int actorHp, double expectedBonus)
    {
        // What: verifies StoicKeyword grants its flat +50% bonus based on the *actor's* own HP
        //       being at or below 25% of max - a desperation bonus for a nearly-defeated
        //       attacker, mirroring Cruel's threshold but checked against the actor.
        // How:  StoicKeyword.GetBonus is `actor.Hp <= 0.25 * actor.MaxHp ? 0.5 : 0.0` against an
        //       actor with maxHp=100 (target HP is fixed at full and irrelevant here). The rows
        //       mirror the Cruel test's boundary probes but applied to actorHp instead of
        //       targetHp: exactly-at-threshold (25, bonus), the opposite extreme (0, bonus),
        //       just-above (26, no bonus), and full health (100, no bonus).
        var keyword = new StoicKeyword();
        var actor = MakeEntity("actor", hp: actorHp, maxHp: 100);
        var target = MakeEntity("target", hp: 100, maxHp: 100);

        Assert.Equal(expectedBonus, keyword.GetBonus(actor, target, actorIsAlly: true, actionId: "", Store));
    }

    [Fact]
    public void PowerKeywordRegistry_ResolvesAllFourHpThresholdKeywordsByName()
    {
        // What: verifies the registry maps all four HP-threshold keyword name strings to their
        //       matching keyword instances, mirroring PowerKeywordRegistry_ResolvesTeamworkByName
        //       in TeamworkKeywordTests but for the full set at once.
        var resolved = PowerKeywordRegistry.Resolve(["Engage", "Cruel", "Empowered", "Stoic"]).ToList();

        Assert.Equal(4, resolved.Count);
        Assert.Contains(resolved, k => k is EngageKeyword);
        Assert.Contains(resolved, k => k is CruelKeyword);
        Assert.Contains(resolved, k => k is EmpoweredKeyword);
        Assert.Contains(resolved, k => k is StoicKeyword);
    }
}
