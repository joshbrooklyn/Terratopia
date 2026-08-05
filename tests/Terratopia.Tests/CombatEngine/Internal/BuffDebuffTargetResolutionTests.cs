using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.Internal;

// CombatRoster.ResolveBuffDebuffTargets is exercised here directly rather than through the full
// engine, the same way TargetingQueryTests reaches GetValidTargets directly: it is the only way to
// query CombatRoster's internal selection logic in isolation from a CombatFunction's own
// once-per-action bookkeeping (see PublicInterface/BuffDebuffTests.cs for that integration).
[Collection("CombatEngineSerial")]
public class BuffDebuffTargetResolutionTests
{
    private static CombatEntity MakeEntity(string id) => new(
        entityId: id, name: id, level: 1,
        maxHp: 100, hp: 100, maxTp: 0, tp: 0,
        power: 10, defense: 10, speed: 10,
        evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

    [Fact]
    public void SelectedTargets_ReturnsTheGivenTargets_LivingOnly_Deduplicated()
    {
        // What: verifies SelectedTargets passes through the action's own chosen targets, but
        //       drops the dead and collapses the repeats a multi-hit action produces.
        // How:  a selectedTargets list with a dead entity and a duplicate live entity resolves to
        //       just the one live entity, once.
        var roster = new CombatRoster(allies: [MakeEntity("ally")], enemies: [MakeEntity("enemy")], new Random(0));

        var alive = MakeEntity("t1");
        var dead  = MakeEntity("t2");
        dead.TakeDamage(alive, 1000, isCrit: false);

        var resolved = roster.ResolveBuffDebuffTargets(alive, BuffDebuffTarget.SelectedTargets, [alive, alive, dead]);

        Assert.Equal(["t1"], resolved.Select(e => e.EntityId));
    }

    [Fact]
    public void Self_ReturnsTheActor()
    {
        var actor  = MakeEntity("actor");
        var roster = new CombatRoster(allies: [actor], enemies: [MakeEntity("enemy")], new Random(0));

        var resolved = roster.ResolveBuffDebuffTargets(actor, BuffDebuffTarget.Self, selectedTargets: []);

        Assert.Equal(["actor"], resolved.Select(e => e.EntityId));
    }

    [Fact]
    public void AllAllies_ReturnsEveryLivingEntityOnTheActorsSide_ActorIncluded()
    {
        // What: verifies AllAllies is relative to the actor's own side (not the player's side)
        //       and includes the actor itself.
        // How:  an enemy-side actor's AllAllies must be the living enemies, actor included, and
        //       must exclude the (dead) third enemy and the allies entirely.
        var actor    = MakeEntity("e1");
        var living2  = MakeEntity("e2");
        var dead     = MakeEntity("e3");
        var roster   = new CombatRoster(allies: [MakeEntity("ally")], enemies: [actor, living2, dead], new Random(0));
        dead.TakeDamage(actor, 1000, isCrit: false);

        var resolved = roster.ResolveBuffDebuffTargets(actor, BuffDebuffTarget.AllAllies, selectedTargets: []);

        Assert.Equal(["e1", "e2"], resolved.Select(e => e.EntityId).OrderBy(id => id));
    }

    [Fact]
    public void AllEnemies_ReturnsEveryLivingEntityOnTheOpposingSide()
    {
        var actor  = MakeEntity("ally");
        var enemy1 = MakeEntity("enemy1");
        var enemy2 = MakeEntity("enemy2");
        var roster = new CombatRoster(allies: [actor], enemies: [enemy1, enemy2], new Random(0));

        var resolved = roster.ResolveBuffDebuffTargets(actor, BuffDebuffTarget.AllEnemies, selectedTargets: []);

        Assert.Equal(["enemy1", "enemy2"], resolved.Select(e => e.EntityId).OrderBy(id => id));
    }

    [Fact]
    public void RandomAlly_ExcludesTheActor()
    {
        // What: verifies RandomAlly never resolves to the actor itself - Self exists for that.
        // How:  a lone ally alongside the actor is the only possible draw across many rolls of a
        //       roster seeded with different Random instances.
        var actor      = MakeEntity("actor");
        var otherAlly  = MakeEntity("other");

        for (int seed = 0; seed < 20; seed++)
        {
            var roster   = new CombatRoster(allies: [actor, otherAlly], enemies: [MakeEntity("enemy")], new Random(seed));
            var resolved = roster.ResolveBuffDebuffTargets(actor, BuffDebuffTarget.RandomAlly, selectedTargets: []);

            Assert.Equal(["other"], resolved.Select(e => e.EntityId));
        }
    }

    [Fact]
    public void RandomAlly_WithNoOtherLivingAlly_ResolvesToEmpty()
    {
        var actor  = MakeEntity("actor");
        var roster = new CombatRoster(allies: [actor], enemies: [MakeEntity("enemy")], new Random(0));

        var resolved = roster.ResolveBuffDebuffTargets(actor, BuffDebuffTarget.RandomAlly, selectedTargets: []);

        Assert.Empty(resolved);
    }

    [Fact]
    public void RandomEnemy_ResolvesToOneOfTheLivingEnemies()
    {
        var actor  = MakeEntity("actor");
        var enemy1 = MakeEntity("enemy1");
        var enemy2 = MakeEntity("enemy2");
        var roster = new CombatRoster(allies: [actor], enemies: [enemy1, enemy2], new Random(0));

        var resolved = roster.ResolveBuffDebuffTargets(actor, BuffDebuffTarget.RandomEnemy, selectedTargets: []);

        Assert.Single(resolved);
        Assert.Contains(resolved[0].EntityId, new[] { "enemy1", "enemy2" });
    }

    [Fact]
    public void RandomEnemy_WithNoLivingEnemies_ResolvesToEmpty()
    {
        var actor = MakeEntity("actor");
        var enemy = MakeEntity("enemy");
        enemy.TakeDamage(actor, 1000, isCrit: false);
        var roster = new CombatRoster(allies: [actor], enemies: [enemy], new Random(0));

        var resolved = roster.ResolveBuffDebuffTargets(actor, BuffDebuffTarget.RandomEnemy, selectedTargets: []);

        Assert.Empty(resolved);
    }
}
