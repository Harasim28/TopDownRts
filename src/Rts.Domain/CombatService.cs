namespace Rts.Domain;

public sealed class CombatService : ICombatService
{
    private readonly BalanceConfig _config;
    public CombatService(BalanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    public Battle Start(MatchId match, Fleet teamA, Fleet teamB)
    {
        ArgumentNullException.ThrowIfNull(teamA);
        ArgumentNullException.ThrowIfNull(teamB);
        if (teamA.Team == teamB.Team) throw new ArgumentException("Команды боя должны различаться");
        return new Battle(match, teamA, teamB);
    }

    public BattleTickResult Tick(Battle battle)
    {
        ArgumentNullException.ThrowIfNull(battle);
        if (battle.IsOver) return new BattleTickResult(battle, 0, 0);
        var damageToB = battle.TeamA.FireAt(battle.TeamB, _config.BaseDamagePerTick, _config.ShieldAbsorbsPerTick);
        var damageToA = battle.TeamB.FireAt(battle.TeamA, _config.BaseDamagePerTick, _config.ShieldAbsorbsPerTick);
        return new BattleTickResult(battle, damageToA, damageToB);
    }
}
