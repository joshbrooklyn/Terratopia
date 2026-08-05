using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Passives;

namespace CombatEngine.DataClasses;

public class CombatEntity
{
    public CombatEntity(string entityId, string name, int level,
        int maxHp, int hp, int maxTp, int tp,
        int power, int defense, int speed,
        float evasion, float critChance, float critModifier,
        IReadOnlyList<string>? passives = null)
    {
        EntityId = entityId; Name = name; Level = level;
        MaxHp = maxHp; Hp = hp; MaxTp = maxTp; Tp = tp;
        _power = power; _defense = defense; _speed = speed;
        Evasion = evasion; CritChance = critChance; CritModifier = critModifier;
        Passives = passives?.ToList() ?? new();
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

    public IReadOnlyList<string> Passives { get; }

    private readonly HashSet<string> _consumedPassives = new();
    public IReadOnlyCollection<string> ConsumedPassives => _consumedPassives;
    private readonly Dictionary<BuffDebuffStat, BuffDebuff> _buffsDebuffs = new();

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

    public void TakeDamage(CombatEntity actor, int damage, bool isCrit = false)
    {
        int oldHp = Hp;
        Hp = Math.Max(0, Hp - damage);
        Logger.Debug($"[combat] ApplyDamage: {actor.Name} -> {Name} oldHp={oldHp} damage={damage} isCrit={isCrit} -> newHp={Hp}");
        CombatEventBus.RaiseEntityDamaged(EntityId, Name, damage, actor.EntityId, actor.Name, isCrit, oldHp, Hp);
        if (Hp == 0 && !IsDead)
            HandleDefeat();
    }

    private void HandleDefeat()
    {
        foreach (var passive in PassiveRegistry.GetForTrigger<DeathPassive>(Passives, PassiveTrigger.OnDeath))
        {
            if (passive.TryPreventDeath(this))
                return;
        }

        MarkDead();
        CombatEventBus.RaiseEntityDeath(EntityId, Name);
    }

    // Healing never revives - a dead target is skipped outright, and Hp is capped at MaxHp.
    public void Heal(CombatEntity actor, int amount)
    {
        if (IsDead || amount <= 0)
        {
            Logger.Debug($"[combat] ApplyHeal: {actor.Name} -> {Name} amount={amount} isDead={IsDead} -> skipped");
            return;
        }

        int oldHp = Hp;
        Hp = Math.Min(MaxHp, Hp + amount);
        Logger.Debug($"[combat] ApplyHeal: {actor.Name} -> {Name} oldHp={oldHp} amount={amount} -> newHp={Hp}");
        CombatEventBus.RaiseEntityHealed(EntityId, Name, Hp - oldHp, actor.EntityId, actor.Name, oldHp, Hp);
    }

    public void SpendTp(int amount)
    {
        if (amount <= 0)
        {
            Logger.Debug($"[combat] DeductTp: {Name} amount={amount} -> skipped (non-positive)");
            return;
        }

        int oldTp = Tp;
        Tp -= amount;
        Logger.Debug($"[combat] DeductTp: {Name} oldTp={oldTp} amount={amount} -> newTp={Tp}");
        CombatEventBus.RaiseEntityTpChanged(EntityId, Name, oldTp, Tp);
    }

    // Called by the engine once it has already rolled and decided the attack was evaded.
    // Evasion decays 25% on each successful dodge.
    public void RegisterEvasion(CombatEntity attacker, float roll)
    {
        float oldEvasion = Evasion;
        Evasion = Math.Max(0f, Evasion - (float)CombatBalance.Current.EvasionDecayAmount);
        Logger.Debug($"[combat] TryEvade: {Name} roll={roll:F3} vs evasion={oldEvasion:F3} -> evaded, evasion decayed to {Evasion:F3}");
        CombatEventBus.RaiseAttackEvaded(attacker.EntityId, attacker.Name, EntityId, Name, oldEvasion, Evasion);
    }

    public void MarkDead() => IsDead = true;

    public void Revive(int hp)
    {
        int oldHp = Hp;
        Hp = hp;
        Logger.Debug($"[combat] Revive: {Name} oldHp={oldHp} -> revived at hp={Hp}");
        CombatEventBus.RaiseEntityRevived(EntityId, Name, oldHp, Hp);
    }

    // A stat holds at most one buff/debuff. Re-applying the same polarity refreshes it - the
    // durations add up, the magnitude does not - while the opposite polarity cancels the existing
    // one outright, so neither survives. UntilRemoved is sticky on refresh: if either side of a
    // same-polarity merge is UntilRemoved, the result is UntilRemoved and rounds math is skipped -
    // "lasts until removed" is a stronger guarantee than "lasts N more rounds" and must win either way.
    public void AddBuffDebuff(BuffDebuffStat stat, bool isPositive, int roundsRemaining, bool untilRemoved)
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
                _buffsDebuffs[stat] = existing;
                Logger.Debug($"[combat] AddBuffDebuff: {Name} stat={stat} isPositive={isPositive} rounds={roundsRemaining} untilRemoved={untilRemoved} -> refreshed to {(existing.UntilRemoved ? "untilRemoved" : existing.RoundsRemaining.ToString())}, value {oldValue} -> {GetStatValue(stat)}");
                CombatEventBus.RaiseBuffDebuffApplied(EntityId, Name, stat, isPositive, existing.RoundsRemaining, existing.UntilRemoved, oldValue, GetStatValue(stat));
                return;
            }

            _buffsDebuffs.Remove(stat);
            Logger.Debug($"[combat] AddBuffDebuff: {Name} stat={stat} isPositive={isPositive} -> cancelled existing (isPositive={existing.IsPositive}), value {oldValue} -> {GetStatValue(stat)}");
            CombatEventBus.RaiseBuffDebuffExpired(EntityId, Name, stat, existing.IsPositive, oldValue, GetStatValue(stat));
            return;
        }

        _buffsDebuffs[stat] = new BuffDebuff { Stat = stat, IsPositive = isPositive, RoundsRemaining = roundsRemaining, UntilRemoved = untilRemoved };
        Logger.Debug($"[combat] AddBuffDebuff: {Name} stat={stat} isPositive={isPositive} rounds={roundsRemaining} untilRemoved={untilRemoved} -> applied, value {oldValue} -> {GetStatValue(stat)}");
        CombatEventBus.RaiseBuffDebuffApplied(EntityId, Name, stat, isPositive, roundsRemaining, untilRemoved, oldValue, GetStatValue(stat));
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
                CombatEventBus.RaiseBuffDebuffExpired(EntityId, Name, stat, buff.IsPositive, oldValue, GetStatValue(stat));
            }
            else
            {
                _buffsDebuffs[stat] = buff;
                Logger.Debug($"[combat] TickBuffDebuffs: {Name} stat={stat} isPositive={buff.IsPositive} -> {buff.RoundsRemaining} rounds remaining");
                CombatEventBus.RaiseBuffDebuffTicked(EntityId, Name, stat, buff.IsPositive, buff.RoundsRemaining);
            }
        }
    }

    internal void OnRoundStart()
    {
        TickBuffDebuffs();
    }

    private struct BuffDebuff
    {
        public BuffDebuffStat Stat;
        public bool IsPositive;
        public int RoundsRemaining;
        public bool UntilRemoved;
    }

    public void ConsumePassive(string passiveName) => _consumedPassives.Add(passiveName);

    public bool HasConsumedPassive(string passiveName) => _consumedPassives.Contains(passiveName);
}
