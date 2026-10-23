namespace Rts.Domain;

public sealed record PlayerRating(PlayerId Player, int Points, int Wins, int Losses)
{
    public PlayerRating Win(int points)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(points);
        return this with { Points = Points + points, Wins = Wins + 1 };
    }
    public PlayerRating Lose(int points)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(points);
        return this with { Points = Points - points, Losses = Losses + 1 };
    }
}

public sealed record BattleOutcome
{
    public PlayerId Winner { get; }
    public PlayerId Loser { get; }
    public BattleOutcome(PlayerId winner, PlayerId loser)
    {
        if (winner == loser) throw new ArgumentException("Победитель и проигравший должны различаться");
        Winner = winner; Loser = loser;
    }
}

public interface IRatingRepository
{
    PlayerRating GetOrCreate(PlayerId player);
    (PlayerRating Winner, PlayerRating Loser) RecordOutcome(BattleOutcome outcome);
}
