namespace Rts.Domain;

/// <summary>
/// Тактовая механика боя: каждый живой корабль команды стреляет,
/// суммарный урон тика = число живых кораблей × BaseDamagePerTick.
/// Цель — первый живой корабль противоположной команды; щит гасит
/// min(absorb, щит) урона, остаток идёт в корпус.
/// </summary>
public class CombatService : ICombatService
{
    private readonly BalanceConfig _config;

    public CombatService(BalanceConfig config) => _config = config;

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

        var damage = attacker.Ships.Count(s => !s.Destroyed) * _config.BaseDamagePerTick;
        var target = defender.Ships.First(s => !s.Destroyed);

        var absorbedByShield = Math.Min(_config.ShieldAbsorbsPerTick, target.Shield);
        target.Shield -= absorbedByShield;

        var damageToHull = damage - absorbedByShield;
        target.Hull -= damageToHull;
        return damageToHull;
    }
}