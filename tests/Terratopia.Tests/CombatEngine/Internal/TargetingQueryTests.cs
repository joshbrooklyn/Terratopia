using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.Internal;

[Collection("CombatEngineSerial")]
public class TargetingQueryTests
{
    private static CombatEntity MakeEntity(string id, int speed, int hp = 100, int power = 10) => new(
        entityId: id, name: id, level: 1,
        maxHp: hp, hp: hp, maxTp: 0, tp: 0,
        power: power, defense: 0, speed: speed,
        evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

    // Built purely to query GetValidTargets — it is never resolved, so the CombatFunction is
    // irrelevant here and NoOp keeps it honest.
    private static CombatCommand TargetQuery(string actorId, ValidTarget validTargets, LivingOrDead livingOrDead) => new()
    {
        ActorId        = actorId,
        ValidTargets   = validTargets,
        LivingOrDead   = livingOrDead,
        CombatFunction = NoOpFunction.FunctionName,
    };

    // Every other GetValidTargets_* case (pool selection by ValidTarget/LivingOrDead, for a
    // player-side querying actor) has a public-interface equivalent in
    // PublicInterface/TargetingTests.cs: submitting a TargetingType.Choose command and reading
    // the pool straight off the TargetSelectionRequested event, since CombatFlowMachine builds
    // that event's validTargetIds from this exact same GetValidTargets call (see
    // CombatFlowMachine.WaitingForTargetSelection's OnEntry). This one case can't move there:
    // TargetSelectionRequested is only ever raised for a player actor submitting Choose — a
    // non-player (AI) actor's command always goes through AssignRandomAiTarget instead, which
    // never surfaces its GetValidTargets pool through any public event. Querying it directly is
    // the only way to observe it.
    [Fact]
    public void GetValidTargets_Enemies_FromEnemyActor_ReturnsAllies()
    {
        // What: verifies that ValidTarget.Enemies is relative to the querying actor's own
        //       side, not an absolute label — an enemy-side actor's "Enemies" pool should be
        //       the player's allies, mirroring GetValidTargets_Enemies_FromPlayerActor in
        //       PublicInterface/TargetingTests.cs.
        // How:  InitCombat sets up two allies and one enemy. Calling GetValidTargets with a
        //       query for "enemy" (which IsPlayerEntity will classify as not a player actor)
        //       and ValidTarget.Enemies should resolve to the opposite side from the enemy's
        //       perspective, i.e. the allies list. The test asserts the returned IDs are
        //       exactly ["ally1", "ally2"], proving the pool selection flips based on which
        //       side the actor belongs to.
        var engine = new CombatEngineClass(new Random(0));
        var ally1 = MakeEntity("ally1", speed: 10);
        var ally2 = MakeEntity("ally2", speed: 10);
        var enemy = MakeEntity("enemy", speed: 10);
        engine.InitCombat(allies: [ally1, ally2], enemies: [enemy]);

        var targets = engine.GetValidTargets(TargetQuery("enemy", ValidTarget.Enemies, LivingOrDead.Living));

        Assert.Equal(["ally1", "ally2"], targets.Select(e => e.EntityId));
    }
}
