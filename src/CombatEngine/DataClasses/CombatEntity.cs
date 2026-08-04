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
        Power = power; Defense = defense; Speed = speed;
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
    public int Power { get; }
    public int Defense { get; }
    public int Speed { get; }

    // Secondary stats
    public float Evasion { get; private set; }

    public bool IsDead { get; private set; } = false;
    public float CritChance { get; }
    public float CritModifier { get; } = 0.5f;   // default +50% on crit

    public IReadOnlyList<string> Passives { get; }

    private readonly HashSet<string> _consumedPassives = new();
    public IReadOnlyCollection<string> ConsumedPassives => _consumedPassives;

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
        Evasion = Math.Max(0f, Evasion - 0.25f);
        Logger.Debug($"[combat] TryEvade: {Name} roll={roll:F3} vs evasion={oldEvasion:F3} -> evaded, evasion decayed to {Evasion:F3}");
        CombatEventBus.RaiseAttackEvaded(attacker.EntityId, attacker.Name, EntityId, Name);
    }

    public void MarkDead() => IsDead = true;

    public void Revive(int hp)
    {
        int oldHp = Hp;
        Hp = hp;
        Logger.Debug($"[combat] Revive: {Name} oldHp={oldHp} -> revived at hp={Hp}");
        CombatEventBus.RaiseEntityRevived(EntityId, Name, oldHp, Hp);
    }

    public void ConsumePassive(string passiveName) => _consumedPassives.Add(passiveName);

    public bool HasConsumedPassive(string passiveName) => _consumedPassives.Contains(passiveName);
}
