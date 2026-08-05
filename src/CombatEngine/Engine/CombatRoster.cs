using CombatEngine.DataClasses;
using CombatEngine.Enums;

namespace CombatEngine.Engine;

// Who is in the fight, which side each entity is on, and every selection made over them: the
// valid target pool for a command, auto-target expansion, AI target assignment, and buff/debuff
// target-selector resolution. Owns the rosters outright - CombatEngineClass reaches through this
// type rather than holding entity lists of its own.
//
// One instance per encounter, built by CombatEngineClass.InitCombat, so a new fight starts from
// a clean roster rather than a cleared one. Shares the engine's Random instance: the auto-target,
// AI-target, and RandomAlly/RandomEnemy buff-target paths all draw from it, so their draw order is
// part of the engine's overall sequence.
internal sealed class CombatRoster
{
    private readonly Random _rng;
    private readonly List<CombatEntity> _allies  = new();
    private readonly List<CombatEntity> _enemies = new();
    private readonly Dictionary<string, CombatEntity> _allEntities;

    internal CombatRoster(IReadOnlyList<CombatEntity> allies, IReadOnlyList<CombatEntity> enemies, Random rng)
    {
        _rng = rng;
        _allies.AddRange(allies);
        _enemies.AddRange(enemies);
        _allEntities = _allies.Concat(_enemies).ToDictionary(e => e.EntityId);
    }

    internal IReadOnlyDictionary<string, CombatEntity> AllEntities => _allEntities;

    internal bool IsPlayerEntity(CombatEntity entity) => _allies.Contains(entity);

    internal CombatEntity GetEntity(string entityId) =>
        _allEntities.TryGetValue(entityId, out var entity) && entity != null
            ? entity
            : throw new InvalidOperationException($"Target with ID {entityId} not found among combat entities.");

    internal IReadOnlyList<CombatEntity> GetLivingEntities() =>
        _allEntities.Values.Where(e => !e.IsDead).ToList();

    internal IReadOnlyList<CombatEntity> GetLivingAllies() =>
        _allies.Where(e => !e.IsDead).ToList();

    internal IReadOnlyList<CombatEntity> GetLivingEnemies() =>
        _enemies.Where(e => !e.IsDead).ToList();

    internal IReadOnlyList<CombatEntity> GetValidTargets(CombatCommand cmd)
    {
        var actor = _allEntities[cmd.ActorId];
        bool actorIsPlayer = IsPlayerEntity(actor);

        IEnumerable<CombatEntity> pool = cmd.ValidTargets switch
        {
            ValidTarget.Allies  => actorIsPlayer ? _allies  : _enemies,
            ValidTarget.Enemies => actorIsPlayer ? _enemies : _allies,
            ValidTarget.Both    => _allEntities.Values,
            _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
        };

        return cmd.LivingOrDead switch
        {
            LivingOrDead.Living => pool.Where(e => !e.IsDead).ToList(),
            LivingOrDead.Dead   => pool.Where(e => e.IsDead).ToList(),
            LivingOrDead.Both   => pool.ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
        };
    }

    // Resolves one buffsDebuffs[] entry's target selector to the living entities it lands on,
    // relative to actor - independent of the action's own chosen targets except for
    // SelectedTargets. RandomAlly excludes actor; both Random* draw from the shared _rng, so using
    // one shifts the draw sequence for everything resolved after it.
    internal IReadOnlyList<CombatEntity> ResolveBuffDebuffTargets(
        CombatEntity actor, BuffDebuffTarget selector, IReadOnlyList<CombatEntity> selectedTargets)
    {
        bool actorIsPlayer = IsPlayerEntity(actor);
        var  allies        = actorIsPlayer ? GetLivingAllies()  : GetLivingEnemies();
        var  enemies       = actorIsPlayer ? GetLivingEnemies() : GetLivingAllies();

        return selector switch
        {
            BuffDebuffTarget.SelectedTargets => selectedTargets.Where(e => !e.IsDead).DistinctBy(e => e.EntityId).ToList(),
            BuffDebuffTarget.Self             => new[] { actor },
            BuffDebuffTarget.RandomAlly       => DrawOne(allies.Where(e => e != actor).ToList(), _rng),
            BuffDebuffTarget.RandomEnemy      => DrawOne(enemies, _rng),
            BuffDebuffTarget.AllAllies        => allies,
            BuffDebuffTarget.AllEnemies       => enemies,
            _ => throw new ArgumentOutOfRangeException(nameof(selector)),
        };
    }

    private static IReadOnlyList<CombatEntity> DrawOne(IReadOnlyList<CombatEntity> pool, Random rng) =>
        pool.Count == 0 ? Array.Empty<CombatEntity>() : new[] { pool[rng.Next(pool.Count)] };

    internal void AssignRandomAiTarget(CombatCommand cmd)
    {
        var pool = GetValidTargets(cmd);
        cmd.ChosenTargets = new List<string> { pool[_rng.Next(pool.Count)].EntityId };
    }

    internal void ExpandAutoTargets(CombatCommand cmd)
    {
        switch (cmd.TargetingType)
        {
            case TargetingType.All:
            {
                bool actorIsPlayer = IsPlayerEntity(_allEntities[cmd.ActorId]);
                IEnumerable<CombatEntity> allPool = cmd.ValidTargets switch
                {
                    ValidTarget.Allies  => actorIsPlayer ? GetLivingAllies()  : GetLivingEnemies(),
                    ValidTarget.Enemies => actorIsPlayer ? GetLivingEnemies() : GetLivingAllies(),
                    ValidTarget.Both    => GetLivingEntities(),
                    _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
                };
                cmd.ChosenTargets = allPool.Select(e => e.EntityId).ToList();
                break;
            }
            case TargetingType.Self:
                cmd.ChosenTargets = new List<string> { cmd.ActorId };
                break;
            case TargetingType.Random:
            {
                var pool = IsPlayerEntity(_allEntities[cmd.ActorId])
                    ? GetLivingEnemies()
                    : GetLivingAllies();
                int picks = ResolveRequiredPickCount(cmd.NumAttacks, cmd.AllowMultipleAttackOnSameTarget, pool.Count);
                cmd.ChosenTargets = cmd.AllowMultipleAttackOnSameTarget
                    ? PickWithReplacement(pool, picks, _rng)
                    : PickDistinctWithoutReplacement(pool, picks, _rng);
                break;
            }
        }
    }

    internal static int ResolveRequiredPickCount(int numAttacks, bool allowMultipleAttackOnSameTarget, int poolSize) =>
        allowMultipleAttackOnSameTarget ? numAttacks : Math.Min(numAttacks, poolSize);

    private static List<string> PickWithReplacement(IReadOnlyList<CombatEntity> pool, int count, Random rng)
    {
        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
            result.Add(pool[rng.Next(pool.Count)].EntityId);
        return result;
    }

    private static List<string> PickDistinctWithoutReplacement(IReadOnlyList<CombatEntity> pool, int count, Random rng)
    {
        var remaining = pool.ToList();
        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int idx = rng.Next(remaining.Count);
            result.Add(remaining[idx].EntityId);
            remaining.RemoveAt(idx);
        }
        return result;
    }
}
