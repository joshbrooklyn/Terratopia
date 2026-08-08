using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.TriggeredEffects;

namespace CombatEngine.DataClasses;

public class CombatEntity
{
    public CombatEntity(string entityId, string name, int level,
        int maxHp, int hp, int maxTp, int tp,
        int power, int defense, int speed,
        float evasion, float critChance, float critModifier)
    {
        EntityId = entityId; Name = name; Level = level;
        MaxHp = maxHp; Hp = hp; MaxTp = maxTp; Tp = tp;
        _power = power; _defense = defense; _speed = speed;
        Evasion = evasion; CritChance = critChance; CritModifier = critModifier;
    }

    public string EntityId { get; }
    public string Name { get; }

    // Primary stats
    public int Level { get; }
    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public int MaxTp { get; }     // adventurers only; 0 for enemies
    public int Tp { get; private set; }

    // Buffable stats. The base values are immutable; the public getters layer the entity's
    // current buff/debuff on top, so every existing reader (CombatMath's Power/Defense,
    // TurnOrderManager's Speed) picks a buff up with no change of its own.
    private readonly int _power;
    private readonly int _defense;
    private readonly int _speed;

    public int Power   => ApplyBuffDebuff(BuffDebuffStat.Power,   _power);
    public int Defense => ApplyBuffDebuff(BuffDebuffStat.Defense, _defense);
    public int Speed   => ApplyBuffDebuff(BuffDebuffStat.Speed,   _speed);

    // Secondary stats
    public float Evasion { get; private set; }

    public bool IsDead { get; private set; } = false;
    public float CritChance { get; }
    public float CritModifier { get; }

    private readonly Dictionary<BuffDebuffStat, BuffDebuff> _buffsDebuffs = new();
    private readonly Dictionary<RegenDrainStat, RegenDrain> _regensDrains = new();

    // At most one buff or debuff per stat, and every one of them moves the stat by the same
    // global magnitude - polarity is the only thing that varies per buff.
    private int ApplyBuffDebuff(BuffDebuffStat stat, int baseValue)
    {
        if (!_buffsDebuffs.TryGetValue(stat, out var buff))
            return baseValue;

        double pct = buff.IsPositive ? CombatBalance.Current.TimedBuffPct : -CombatBalance.Current.TimedBuffPct;
        return (int)Math.Round(baseValue * (1 + pct));
    }

    private int GetStatValue(BuffDebuffStat stat) => stat switch
    {
        BuffDebuffStat.Power   => Power,
        BuffDebuffStat.Defense => Defense,
        BuffDebuffStat.Speed   => Speed,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "No buffable stat for this BuffDebuffStat."),
    };

    public void TakeDamage(CombatEntity actor, int damage, string sourceId, string sourceName, bool isCrit = false)
    {
        int oldHp = Hp;
        Hp = Math.Max(0, Hp - damage);
        Logger.Debug($"[combat] ApplyDamage: {actor.Name} -> {Name} oldHp={oldHp} damage={damage} isCrit={isCrit} -> newHp={Hp}");
        CombatEventBus.RaiseEntityDamaged(EntityId, Name, damage, actor.EntityId, actor.Name, sourceId, sourceName, isCrit, oldHp, Hp);
        if (Hp == 0 && !IsDead)
            HandleDefeat(sourceId, sourceName);
    }

    private void HandleDefeat(string sourceId, string sourceName)
    {
        foreach (var triggeredEffect in TriggeredEffectTracker.GetTriggeredEffects(EntityId))
        {
            var (deathPrevented, reviveHp) = triggeredEffect.OnBeforeDeath(this.EntityId, this.Name);

            if (deathPrevented)
            {
                int oldHp = Hp;
                Hp = reviveHp;
                Logger.Debug($"[combat] Revive: {Name} oldHp={oldHp} -> revived at hp={Hp}");
                CombatEventBus.RaiseEntityRevived(EntityId, Name, oldHp, Hp, sourceId, sourceName);

                return; // only one triggered effect can prevent death, so stop after the first one that does
            }
        }

        // Cancel-on-own-death entries go before MarkDead()/RaiseEntityDeath, so no listener -
        // including CombatRoster's applier-death broadcast to other entities - ever observes this
        // entity as dead while one of its own CancelOnEntityDeath entries is still active.
        CancelBuffsDebuffsIf(b => b.CancelOnEntityDeath);
        CancelRegensDrainsIf(r => r.CancelOnEntityDeath);

        MarkDead();
        CombatEventBus.RaiseEntityDeath(EntityId, Name, sourceId, sourceName);
    }

    // Healing never revives - a dead target is skipped outright, and Hp is capped at MaxHp.
    public void Heal(CombatEntity actor, int amount, string sourceId, string sourceName)
    {
        if (IsDead || amount <= 0)
        {
            Logger.Debug($"[combat] ApplyHeal: {actor.Name} -> {Name} amount={amount} isDead={IsDead} -> skipped");
            return;
        }

        int oldHp = Hp;
        Hp = Math.Min(MaxHp, Hp + amount);
        Logger.Debug($"[combat] ApplyHeal: {actor.Name} -> {Name} oldHp={oldHp} amount={amount} -> newHp={Hp}");
        CombatEventBus.RaiseEntityHealed(EntityId, Name, Hp - oldHp, actor.EntityId, actor.Name, sourceId, sourceName, oldHp, Hp);
    }

    public void SpendTp(int amount, string sourceId, string sourceName)
    {
        if (amount <= 0)
        {
            Logger.Debug($"[combat] DeductTp: {Name} amount={amount} -> skipped (non-positive)");
            return;
        }

        int oldTp = Tp;
        Tp = Math.Max(0, Tp - amount);
        Logger.Debug($"[combat] DeductTp: {Name} oldTp={oldTp} amount={amount} -> newTp={Tp}");
        CombatEventBus.RaiseEntityTpChanged(EntityId, Name, oldTp, Tp, sourceId, sourceName);
    }

    // Mirrors Heal: caps at MaxTp, no-ops on a non-positive amount.
    public void RestoreTp(int amount, string sourceId, string sourceName)
    {
        if (amount <= 0)
        {
            Logger.Debug($"[combat] RestoreTp: {Name} amount={amount} -> skipped (non-positive)");
            return;
        }

        int oldTp = Tp;
        Tp = Math.Min(MaxTp, Tp + amount);
        Logger.Debug($"[combat] RestoreTp: {Name} oldTp={oldTp} amount={amount} -> newTp={Tp}");
        CombatEventBus.RaiseEntityTpChanged(EntityId, Name, oldTp, Tp, sourceId, sourceName);
    }

    // Called by the engine once it has already rolled and decided the attack was evaded.
    // Evasion decays 25% on each successful dodge.
    public void RegisterEvasion(CombatEntity attacker, float roll, string sourceId, string sourceName)
    {
        float oldEvasion = Evasion;
        Evasion = Math.Max(0f, Evasion - (float)CombatBalance.Current.EvasionDecayAmount);
        Logger.Debug($"[combat] TryEvade: {Name} roll={roll:F3} vs evasion={oldEvasion:F3} -> evaded, evasion decayed to {Evasion:F3}");
        CombatEventBus.RaiseAttackEvaded(attacker.EntityId, attacker.Name, EntityId, Name, oldEvasion, Evasion, sourceId, sourceName);
    }

    public void MarkDead() => IsDead = true;

    // A stat holds at most one buff/debuff. Re-applying the same polarity refreshes it - the
    // durations add up, the magnitude does not - while the opposite polarity cancels the existing
    // one outright, so neither survives. UntilRemoved is sticky on refresh: if either side of a
    // same-polarity merge is UntilRemoved, the result is UntilRemoved and rounds math is skipped -
    // "lasts until removed" is a stronger guarantee than "lasts N more rounds" and must win either way.
    public void AddBuffDebuff(BuffDebuffStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, string sourceId, string sourceName,
        string applierId = "", bool cancelOnEntityDeath = true, bool cancelOnApplierDeath = false)
    {
        if (!untilRemoved && roundsRemaining <= 0)
        {
            Logger.Debug($"[combat] AddBuffDebuff: {Name} stat={stat} isPositive={isPositive} rounds={roundsRemaining} -> skipped (non-positive rounds)");
            return;
        }

        int oldValue = GetStatValue(stat);

        if (_buffsDebuffs.TryGetValue(stat, out var existing))
        {
            if (existing.IsPositive == isPositive)
            {
                existing.UntilRemoved = existing.UntilRemoved || untilRemoved;
                if (!existing.UntilRemoved)
                {
                    existing.RoundsRemaining += roundsRemaining;
                }
                // The newest application's source is the one the player just saw, so it wins on refresh.
                existing.SourceId = sourceId;
                existing.SourceName = sourceName;
                // Same rule extends to the applier and cancel-on-death flags.
                existing.ApplierId = applierId;
                existing.CancelOnEntityDeath = cancelOnEntityDeath;
                existing.CancelOnApplierDeath = cancelOnApplierDeath;
                _buffsDebuffs[stat] = existing;
                Logger.Debug($"[combat] AddBuffDebuff: {Name} stat={stat} isPositive={isPositive} rounds={roundsRemaining} untilRemoved={untilRemoved} -> refreshed to {(existing.UntilRemoved ? "untilRemoved" : existing.RoundsRemaining.ToString())}, value {oldValue} -> {GetStatValue(stat)}");
                CombatEventBus.RaiseBuffDebuffApplied(EntityId, Name, stat, isPositive, existing.RoundsRemaining, existing.UntilRemoved, oldValue, GetStatValue(stat), sourceId, sourceName);
                return;
            }

            _buffsDebuffs.Remove(stat);
            Logger.Debug($"[combat] AddBuffDebuff: {Name} stat={stat} isPositive={isPositive} -> cancelled existing (isPositive={existing.IsPositive}), value {oldValue} -> {GetStatValue(stat)}");
            CombatEventBus.RaiseBuffDebuffExpired(EntityId, Name, stat, existing.IsPositive, oldValue, GetStatValue(stat), existing.SourceId, existing.SourceName, sourceId, sourceName);
            return;
        }

        _buffsDebuffs[stat] = new BuffDebuff
        {
            Stat = stat, IsPositive = isPositive, RoundsRemaining = roundsRemaining, UntilRemoved = untilRemoved,
            SourceId = sourceId, SourceName = sourceName,
            ApplierId = applierId, CancelOnEntityDeath = cancelOnEntityDeath, CancelOnApplierDeath = cancelOnApplierDeath,
        };
        Logger.Debug($"[combat] AddBuffDebuff: {Name} stat={stat} isPositive={isPositive} rounds={roundsRemaining} untilRemoved={untilRemoved} -> applied, value {oldValue} -> {GetStatValue(stat)}");
        CombatEventBus.RaiseBuffDebuffApplied(EntityId, Name, stat, isPositive, roundsRemaining, untilRemoved, oldValue, GetStatValue(stat), sourceId, sourceName);
    }

    public void TickBuffDebuffs()
    {
        // Snapshot the keys so an expiring entry can be removed inside the loop.
        foreach (var stat in _buffsDebuffs.Keys.ToList())
        {
            var buff = _buffsDebuffs[stat];

            // UntilRemoved entries never tick - they're removed only by opposite-polarity
            // cancellation in AddBuffDebuff, never by the round clock.
            if (buff.UntilRemoved)
            {
                continue;
            }

            int oldValue = GetStatValue(stat);
            buff.RoundsRemaining--;

            if (buff.RoundsRemaining <= 0)
            {
                _buffsDebuffs.Remove(stat);
                Logger.Debug($"[combat] TickBuffDebuffs: {Name} stat={stat} isPositive={buff.IsPositive} -> expired, value {oldValue} -> {GetStatValue(stat)}");
                CombatEventBus.RaiseBuffDebuffExpired(EntityId, Name, stat, buff.IsPositive, oldValue, GetStatValue(stat), buff.SourceId, buff.SourceName, "", "");
            }
            else
            {
                _buffsDebuffs[stat] = buff;
                Logger.Debug($"[combat] TickBuffDebuffs: {Name} stat={stat} isPositive={buff.IsPositive} -> {buff.RoundsRemaining} rounds remaining");
                CombatEventBus.RaiseBuffDebuffTicked(EntityId, Name, stat, buff.IsPositive, buff.RoundsRemaining, buff.SourceId, buff.SourceName);
            }
        }
    }

    // A resource holds at most one regen/drain, following the same merge/cancel/sticky-UntilRemoved
    // rules as AddBuffDebuff. Unlike a buff/debuff, applying an entry changes nothing by itself -
    // the HP/TP delta only lands once ApplyRegensDrains runs - so there is no oldValue/newValue to
    // report on this event.
    public void AddRegenDrain(RegenDrainStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, string sourceId, string sourceName,
        string applierId = "", bool cancelOnEntityDeath = true, bool cancelOnApplierDeath = false)
    {
        if (!untilRemoved && roundsRemaining <= 0)
        {
            Logger.Debug($"[combat] AddRegenDrain: {Name} stat={stat} isPositive={isPositive} rounds={roundsRemaining} -> skipped (non-positive rounds)");
            return;
        }

        if (_regensDrains.TryGetValue(stat, out var existing))
        {
            if (existing.IsPositive == isPositive)
            {
                existing.UntilRemoved = existing.UntilRemoved || untilRemoved;
                if (!existing.UntilRemoved)
                {
                    existing.RoundsRemaining += roundsRemaining;
                }
                // The newest application's source is the one the player just saw, so it wins on refresh.
                existing.SourceId = sourceId;
                existing.SourceName = sourceName;
                // Same rule extends to the applier and cancel-on-death flags.
                existing.ApplierId = applierId;
                existing.CancelOnEntityDeath = cancelOnEntityDeath;
                existing.CancelOnApplierDeath = cancelOnApplierDeath;
                _regensDrains[stat] = existing;
                Logger.Debug($"[combat] AddRegenDrain: {Name} stat={stat} isPositive={isPositive} rounds={roundsRemaining} untilRemoved={untilRemoved} -> refreshed to {(existing.UntilRemoved ? "untilRemoved" : existing.RoundsRemaining.ToString())}");
                CombatEventBus.RaiseRegenDrainApplied(EntityId, Name, stat, isPositive, existing.RoundsRemaining, existing.UntilRemoved, sourceId, sourceName);
                return;
            }

            _regensDrains.Remove(stat);
            Logger.Debug($"[combat] AddRegenDrain: {Name} stat={stat} isPositive={isPositive} -> cancelled existing (isPositive={existing.IsPositive})");
            CombatEventBus.RaiseRegenDrainExpired(EntityId, Name, stat, existing.IsPositive, existing.SourceId, existing.SourceName, sourceId, sourceName);
            return;
        }

        _regensDrains[stat] = new RegenDrain
        {
            Stat = stat, IsPositive = isPositive, RoundsRemaining = roundsRemaining, UntilRemoved = untilRemoved,
            SourceId = sourceId, SourceName = sourceName,
            ApplierId = applierId, CancelOnEntityDeath = cancelOnEntityDeath, CancelOnApplierDeath = cancelOnApplierDeath,
        };
        Logger.Debug($"[combat] AddRegenDrain: {Name} stat={stat} isPositive={isPositive} rounds={roundsRemaining} untilRemoved={untilRemoved} -> applied");
        CombatEventBus.RaiseRegenDrainApplied(EntityId, Name, stat, isPositive, roundsRemaining, untilRemoved, sourceId, sourceName);
    }

    // Applies every active regen/drain's HP/TP delta for this round. A flat percentage of the
    // resource's max, computed directly here rather than through CombatMath - no Defense
    // mitigation, no element, matching the design ("no elemental component"). Routes through the
    // existing TakeDamage/Heal/SpendTp/RestoreTp so clamping, death handling (HandleDefeat/OnDeath
    // triggered effects), and the usual EntityDamaged/EntityHealed/EntityTpChanged events all fire exactly
    // as they would for any other source. The entity itself is passed as the "actor" - the
    // sourceId/sourceName reported are the tech/item that originally inflicted the regen/drain.
    private void ApplyRegensDrains()
    {
        if (IsDead)
            return;

        // Snapshot the values: a lethal Hp Drain below can synchronously reach HandleDefeat's
        // own-death sweep, which mutates _regensDrains mid-loop.
        foreach (var regenDrain in _regensDrains.Values.ToList())
        {
            switch (regenDrain.Stat)
            {
                case RegenDrainStat.Hp:
                    int hpAmount = (int)Math.Round(MaxHp * CombatBalance.Current.RegenDrainHpPct);
                    if (regenDrain.IsPositive)
                        Heal(this, hpAmount, regenDrain.SourceId, regenDrain.SourceName);
                    else
                        TakeDamage(this, hpAmount, regenDrain.SourceId, regenDrain.SourceName);
                    break;
                case RegenDrainStat.Tp:
                    int tpAmount = (int)Math.Round(MaxTp * CombatBalance.Current.RegenDrainTpPct);
                    if (regenDrain.IsPositive)
                        RestoreTp(tpAmount, regenDrain.SourceId, regenDrain.SourceName);
                    else
                        SpendTp(tpAmount, regenDrain.SourceId, regenDrain.SourceName);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(regenDrain.Stat), regenDrain.Stat, "No resource for this RegenDrainStat.");
            }

            // A drain that kills the entity mid-loop shouldn't also apply any of its remaining
            // entries this round.
            if (IsDead)
                return;
        }
    }

    public void TickRegensDrains()
    {
        // Snapshot the keys so an expiring entry can be removed inside the loop.
        foreach (var stat in _regensDrains.Keys.ToList())
        {
            var regenDrain = _regensDrains[stat];

            // UntilRemoved entries never tick - they're removed only by opposite-polarity
            // cancellation in AddRegenDrain, never by the round clock.
            if (regenDrain.UntilRemoved)
            {
                continue;
            }

            regenDrain.RoundsRemaining--;

            if (regenDrain.RoundsRemaining <= 0)
            {
                _regensDrains.Remove(stat);
                Logger.Debug($"[combat] TickRegensDrains: {Name} stat={stat} isPositive={regenDrain.IsPositive} -> expired");
                CombatEventBus.RaiseRegenDrainExpired(EntityId, Name, stat, regenDrain.IsPositive, regenDrain.SourceId, regenDrain.SourceName, "", "");
            }
            else
            {
                _regensDrains[stat] = regenDrain;
                Logger.Debug($"[combat] TickRegensDrains: {Name} stat={stat} isPositive={regenDrain.IsPositive} -> {regenDrain.RoundsRemaining} rounds remaining");
                CombatEventBus.RaiseRegenDrainTicked(EntityId, Name, stat, regenDrain.IsPositive, regenDrain.RoundsRemaining, regenDrain.SourceId, regenDrain.SourceName);
            }
        }
    }

    // Shared by HandleDefeat's own-death sweep and CancelEffectsAppliedBy's cross-entity sweep.
    // Mirrors TickBuffDebuffs/TickRegensDrains's snapshot-then-remove shape, and raises the same
    // *Expired events a natural tick-expiry would, with empty counteredBy fields - a cancellation
    // caused by a death is not an opposite-polarity countering.
    private void CancelBuffsDebuffsIf(Func<BuffDebuff, bool> predicate)
    {
        foreach (var stat in _buffsDebuffs.Keys.ToList())
        {
            var buff = _buffsDebuffs[stat];
            if (!predicate(buff))
                continue;

            int oldValue = GetStatValue(stat);
            _buffsDebuffs.Remove(stat);
            Logger.Debug($"[combat] CancelBuffsDebuffsIf: {Name} stat={stat} isPositive={buff.IsPositive} -> cancelled, value {oldValue} -> {GetStatValue(stat)}");
            CombatEventBus.RaiseBuffDebuffExpired(EntityId, Name, stat, buff.IsPositive, oldValue, GetStatValue(stat), buff.SourceId, buff.SourceName, "", "");
        }
    }

    private void CancelRegensDrainsIf(Func<RegenDrain, bool> predicate)
    {
        foreach (var stat in _regensDrains.Keys.ToList())
        {
            var regenDrain = _regensDrains[stat];
            if (!predicate(regenDrain))
                continue;

            _regensDrains.Remove(stat);
            Logger.Debug($"[combat] CancelRegensDrainsIf: {Name} stat={stat} isPositive={regenDrain.IsPositive} -> cancelled");
            CombatEventBus.RaiseRegenDrainExpired(EntityId, Name, stat, regenDrain.IsPositive, regenDrain.SourceId, regenDrain.SourceName, "", "");
        }
    }

    // Called by CombatRoster when CombatEventBus.EntityDeath fires for some OTHER entity - sweeps
    // this entity's own buffs/debuffs and regens/drains for entries sourced from that entity
    // (ApplierId == applierId) whose CancelOnApplierDeath is set. Never called for an entity's own
    // death; that path is CancelOnEntityDeath's job inside HandleDefeat, run before EntityDeath is
    // even raised.
    public void CancelEffectsAppliedBy(string applierId)
    {
        CancelBuffsDebuffsIf(b => b.ApplierId == applierId && b.CancelOnApplierDeath);
        CancelRegensDrainsIf(r => r.ApplierId == applierId && r.CancelOnApplierDeath);
    }

    // Split from the regen/drain phase so the engine can tick Speed buffs (which affect turn
    // order) before BuildRound, while landing the regen/drain HP/TP delta - and its combat-log
    // entries - after RoundStarted fires, so the log reads as "round begins, then regen ticks"
    // rather than appearing to land at the tail of the previous round.
    internal void ProcessRegensDrains()
    {
        // ApplyRegensDrains lands the HP/TP delta first, so a rounds:1 entry always fires exactly
        // once before TickRegensDrains removes it.
        ApplyRegensDrains();
        TickRegensDrains();
    }

    private struct BuffDebuff
    {
        public BuffDebuffStat Stat;
        public bool IsPositive;
        public int RoundsRemaining;
        public bool UntilRemoved;
        public string SourceId;
        public string SourceName;
        public string ApplierId;
        public bool CancelOnEntityDeath;
        public bool CancelOnApplierDeath;
    }

    private struct RegenDrain
    {
        public RegenDrainStat Stat;
        public bool IsPositive;
        public int RoundsRemaining;
        public bool UntilRemoved;
        public string SourceId;
        public string SourceName;
        public string ApplierId;
        public bool CancelOnEntityDeath;
        public bool CancelOnApplierDeath;
    }
}
