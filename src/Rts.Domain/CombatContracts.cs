namespace Rts.Domain;

public sealed record Battle(MatchId Match, Fleet TeamA, Fleet TeamB)
{
    public bool IsOver => TeamA.IsWiped || TeamB.IsWiped;
}

public sealed record BattleTickResult(Battle Battle, int HullDamageToTeamA, int HullDamageToTeamB);

public interface ICombatService
{
    Battle Start(MatchId match, Fleet teamA, Fleet teamB);
    BattleTickResult Tick(Battle battle);
}
