using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Enums;
using CombatEngine.Keywords;
using CombatEngine.Passives;

namespace CombatEngine.Engine;

public class CombatEngineClass
{
    private static readonly Lazy<CombatEngineClass> _instance = new(() => new CombatEngineClass());
    public static CombatEngineClass Instance => _instance.Value;
    private readonly Random _rng;
    private readonly TurnOrderManager _turnOrder;
    private CombatFlowMachine _combatFlowMachine = null!;
    private CombatRoster      _roster            = null!;
    private KeywordResolver   _keywords          = null!;

    private int _roundNumber;

    private CombatEngineClass()
    {
        _rng = new();
        _turnOrder = new TurnOrderManager(_rng);
    }

    internal CombatEngineClass(Random rng)
    {
        _rng = rng;
        _turnOrder = new TurnOrderManager(rng);
    }

    public void InitCombat(
        IReadOnlyList<CombatEntity> allies,
        IReadOnlyList<CombatEntity> enemies,
        bool isBossFight = false)
    {
        CombatEventBus.Reset();
        this.Reset();

        _roster   = new CombatRoster(allies, enemies, _rng);
        _keywords = new KeywordResolver();

        _combatFlowMachine = new CombatFlowMachine(
            isPlayerEntity:           _roster.IsPlayerEntity,
            resolveAction:            ResolveAction,
            buildRound:               BuildRound,
            doRoundEnd:               DoRoundEnd,
            isRoundOver:              IsRoundOver,
            evaluateWinCondition:     EvaluateWinCondition,
            getValidTargets:          _roster.GetValidTargets,
            expandAutoTargets:        _roster.ExpandAutoTargets,
            assignAiTarget:           _roster.AssignRandomAiTarget,
            nextTurn:                 NextTurn,
            resolvePickCount:         CombatRoster.ResolveRequiredPickCount
        );
    }

    public void SubmitTargets(List<string> chosenTargetIds) =>
        _combatFlowMachine.SubmitTargets(chosenTargetIds);

    public void SubmitCommand(CombatCommand cmd) =>
        _combatFlowMachine.SubmitCommand(cmd);

    public void BeginCombat()
    {
        _combatFlowMachine.Start();
    }

    // The roster and keyword counters are rebuilt outright by InitCombat rather than cleared, so
    // Reset only has to drop the previous encounter's collaborators.
    private void Reset()
    {
        CombatEventBus.Reset();
        _roundNumber       = 0;
        _roster            = null!;
        _keywords          = null!;
        _combatFlowMachine = null!;
    }

    internal IReadOnlyList<CombatEntity> GetValidTargets(CombatCommand cmd) =>
        _roster.GetValidTargets(cmd);

    private void BuildRound()
    {
        _roundNumber++;
        _turnOrder.BuildRound(_roster.GetLivingEntities());
        CombatEventBus.RaiseRoundStarted(_roundNumber, _turnOrder.CurrentTurnOrderIds, _turnOrder.CurrentTurnOrderNames);
    }

    private void DoRoundEnd()
    {
        CombatEventBus.RaiseRoundEnded(_roundNumber);
    }

    private CombatEntity? NextTurn()
    {
        var entity = _turnOrder.NextTurn();
        return entity;
    }


    private void ResolveAction(CombatCommand cmd)
    {
        if (!_roster.AllEntities.TryGetValue(cmd.ActorId, out var actor) || actor == null)
            throw new InvalidOperationException($"Actor with ID {cmd.ActorId} not found among combat entities.");

        var function = CombatFunctionRegistry.Resolve(cmd.CombatFunction);

        bool actorIsAlly    = _roster.IsPlayerEntity(actor);
        var  activeKeywords = PowerKeywordRegistry.Resolve(cmd.Keywords).ToList();
        _keywords.NotifyKeywordsUsed(activeKeywords, actor, actorIsAlly, cmd.ActionId);

        var targets = cmd.ChosenTargets.Select(_roster.GetEntity).ToList();

        function.Execute(new CombatFunctionContext
        {
            Command             = cmd,
            Actor               = actor,
            ActorIsAlly         = actorIsAlly,
            Parameters          = cmd.Parameters,
            Targets             = targets,
            AllEntities         = _roster.AllEntities,
            GetEntity           = _roster.GetEntity,
            Rng                 = _rng,
            ResolveTpCost       = ()                => cmd.TPCost,
            DeductTp            = DeductTp,
            TryEvade            = TryEvade,
            RollCrit            = RollCrit,
            ApplyCritModifier   = ApplyCritModifier,
            ApplyKeywordBonuses = (basePower, a, t) => _keywords.ApplyKeywordBonuses(activeKeywords, basePower, a, t, actorIsAlly, cmd.ActionId),
            CalculateDamage     = CombatMath.CalculateDamage,
            CalculateHealAmount = CombatMath.CalculateHealAmount,
            ApplyDamage         = ApplyDamage,
            ApplyHeal           = ApplyHeal,
        });

        CombatEventBus.RaiseActionResolved(cmd, actor.Name, targets.Select(t => t.Name).ToList());
    }

    private static void HandleEntityDefeated(CombatEntity target)
    {
        foreach (var passive in PassiveRegistry.GetForTrigger<DeathPassive>(target.Passives, PassiveTrigger.OnDeath))
        {
            if (passive.TryPreventDeath(target))
                return;
        }

        target.IsDead = true;
        CombatEventBus.RaiseEntityDeath(target.EntityId, target.Name);
    }

    private static void DeductTp(CombatEntity actor, int amount)
    {
        if (amount <= 0)
        {
            Logger.Debug($"[combat] DeductTp: {actor.Name} amount={amount} -> skipped (non-positive)");
            return;
        }

        int oldTp = actor.Tp;
        actor.Tp -= amount;
        Logger.Debug($"[combat] DeductTp: {actor.Name} oldTp={oldTp} amount={amount} -> newTp={actor.Tp}");
        CombatEventBus.RaiseEntityTpChanged(actor.EntityId, actor.Name, oldTp, actor.Tp);
    }

    // True when the attack is evaded. Evasion decays 25% on each successful dodge.
    private bool TryEvade(CombatEntity actor, CombatEntity target)
    {
        float roll = _rng.NextSingle();
        if (roll >= target.Evasion)
        {
            Logger.Debug($"[combat] TryEvade: {target.Name} roll={roll:F3} vs evasion={target.Evasion:F3} -> not evaded");
            return false;
        }

        target.Evasion = Math.Max(0f, target.Evasion - 0.25f);
        Logger.Debug($"[combat] TryEvade: {target.Name} roll={roll:F3} vs evasion={target.Evasion + 0.25f:F3} -> evaded, evasion decayed to {target.Evasion:F3}");
        CombatEventBus.RaiseAttackEvaded(actor.EntityId, actor.Name, target.EntityId, target.Name);
        return true;
    }

    private bool RollCrit(CombatEntity a)
    {
        float roll = _rng.NextSingle();
        bool isCrit = roll < a.CritChance;
        Logger.Debug($"[combat] RollCrit: {a.Name} roll={roll:F3} vs critChance={a.CritChance:F3} -> {(isCrit ? "crit" : "no crit")}");
        return isCrit;
    }

    private static int ApplyCritModifier(CombatEntity a, int damage)
    {
        int result = (int)(damage * (1.0f + a.CritModifier));
        Logger.Debug($"[combat] ApplyCritModifier: {a.Name} damage={damage} critModifier={a.CritModifier:F3} -> {result}");
        return result;
    }

    private static void ApplyDamage(CombatEntity actor, CombatEntity target, int damage, bool isCrit)
    {
        int oldHp = target.Hp;
        target.Hp = (int)Math.Max(0f, target.Hp - damage);
        Logger.Debug($"[combat] ApplyDamage: {actor.Name} -> {target.Name} oldHp={oldHp} damage={damage} isCrit={isCrit} -> newHp={target.Hp}");
        CombatEventBus.RaiseEntityDamaged(target.EntityId, target.Name, damage, actor.EntityId, actor.Name, isCrit);
        CombatEventBus.RaiseEntityHpChanged(target.EntityId, target.Name, oldHp, target.Hp);
        if (target.Hp == 0 && !target.IsDead)
            HandleEntityDefeated(target);
    }

    // Healing never revives - a dead target is skipped outright, and Hp is capped at MaxHp.
    private static void ApplyHeal(CombatEntity actor, CombatEntity target, int amount)
    {
        if (target.IsDead || amount <= 0)
        {
            Logger.Debug($"[combat] ApplyHeal: {actor.Name} -> {target.Name} amount={amount} isDead={target.IsDead} -> skipped");
            return;
        }

        int oldHp = target.Hp;
        target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
        Logger.Debug($"[combat] ApplyHeal: {actor.Name} -> {target.Name} oldHp={oldHp} amount={amount} -> newHp={target.Hp}");
        CombatEventBus.RaiseEntityHealed(target.EntityId, target.Name, target.Hp - oldHp, actor.EntityId, actor.Name);
        CombatEventBus.RaiseEntityHpChanged(target.EntityId, target.Name, oldHp, target.Hp);
    }

    private bool EvaluateWinCondition()
    {
        var livingAllies  = _roster.GetLivingAllies();
        var livingEnemies = _roster.GetLivingEnemies();

        if (livingAllies.Count > 0 && livingEnemies.Count > 0)
            return false;

        bool playerWon = livingEnemies.Count == 0 && livingAllies.Count > 0;
        CombatEventBus.RaiseCombatOver(playerWon);
        return true;
    }

    private bool IsRoundOver() => _turnOrder.IsRoundOver;
}
