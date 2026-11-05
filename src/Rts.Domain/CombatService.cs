namespace Rts.Domain;

/// <summary>
/// Тактовая механика боя: каждый живой корабль команды стреляет,
/// суммарный урон тика = число живых кораблей × BaseDamagePerTick.
/// Цель — первый живой корабль противоположной команды; щит гасит
/// min(absorb, damage, щит) урона, остаток идёт в корпус.
/// Аддитивно (позже P11/уклонение): подключаемый IShotDamageModifier
/// перемножает множители по каждому выстрелу до суммирования; без него поведение как раньше.
/// </summary>
public class CombatService : ICombatService, ICombatShotTuning
{
    private readonly BalanceConfig _config;
    private IShotDamageModifier? _shotModifier; // аддитивный крючок (P11-спад, уклонение scout); null — прежнее поведение

    public CombatService(BalanceConfig config) => _config = config;

    /// <summary>Подключить модификатор урона выстрела (P11-спад, уклонение scout); null — прежнее поведение.</summary>
    public void SetShotModifier(IShotDamageModifier? modifier) => _shotModifier = modifier;

    public Battle Start(MatchId match, Fleet teamA, Fleet teamB) => new(match, teamA, teamB);

    public BattleTickResult Tick(Battle battle)
    {
        if (battle.IsOver)
        {
            return new BattleTickResult(battle, 0, 0);
        }

        var damageToB = ApplyVolley(battle.TeamA, battle.TeamB);
        var damageToA = ApplyVolley(battle.TeamB, battle.TeamA);

        return new BattleTickResult(battle, damageToA, damageToB);
    }

    /// <summary>Нанести выстрел живых кораблей attacker по первому живому кораблю defender. Возвращает урон по корпусу.</summary>
    private int ApplyVolley(Fleet attacker, Fleet defender)
    {
        if (attacker.IsWiped || defender.IsWiped)
        {
            return 0;
        }

        var target = defender.Ships.First(s => !s.Destroyed);
        var damage = attacker.Ships.Count(s => !s.Destroyed) * _config.BaseDamagePerTick;
        if (_shotModifier is not null)
        {
            // множитель считается ПО каждому выстрелу (спад от дистанции — дистанция стрелок→цель,
            // уклонение — класс цели); щит гасит за один тик не больше, чем нанесено
            damage = (int)Math.Round(
                attacker.Ships.Where(s => !s.Destroyed)
                    .Sum(ship => _config.BaseDamagePerTick * _shotModifier.ShotMultiplierOf(attacker.Owner, ship, defender.Owner, target)),
                MidpointRounding.AwayFromZero);
        }

        // щит гасит за один тик не больше, чем нанесено (урон ниже регламента гашения — P11-спад):
        // дефолтная ветка (damage = n×Base = 10 > 6) даёт ровно прежние числа
        var absorbed = Math.Min(Math.Min(_config.ShieldAbsorbsPerTick, damage), Math.Max(0, target.Shield));
        var damageToHull = damage - absorbed;
        target.ApplyDamage(damage, absorbed);
        return damageToHull;
    }
}