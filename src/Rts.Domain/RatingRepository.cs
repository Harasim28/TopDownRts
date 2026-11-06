namespace Rts.Domain;

/// <summary>Хранилище рейтинга in-memory: победителю +points, проигравшему −points, счётчики растут.</summary>
public class RatingRepository : IRatingRepository
{
    private readonly BalanceConfig _config;
    private readonly Dictionary<PlayerId, PlayerRating> _ratings = new();

    public RatingRepository(BalanceConfig config) => _config = config;

    public PlayerRating GetOrCreate(PlayerId player) =>
        _ratings.TryGetValue(player, out var rating) ? rating : new PlayerRating(player, 0, 0, 0);

    public (PlayerRating Winner, PlayerRating Loser) RecordOutcome(BattleOutcome outcome)
    {
        var winner = GetOrCreate(outcome.Winner);
        var loser = GetOrCreate(outcome.Loser);

        winner = new PlayerRating(
            winner.Player,
            winner.Points + _config.RatingWinPoints,
            winner.Wins + 1,
            winner.Losses);
        loser = new PlayerRating(
            loser.Player,
            loser.Points - _config.RatingLosePoints,
            loser.Wins,
            loser.Losses + 1);

        _ratings[winner.Player] = winner;
        _ratings[loser.Player] = loser;

        return (winner, loser);
    }
}