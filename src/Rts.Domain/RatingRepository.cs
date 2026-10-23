namespace Rts.Domain;

public sealed class RatingRepository : IRatingRepository
{
    private readonly BalanceConfig _config;
    private readonly Dictionary<PlayerId, PlayerRating> _ratings = new();
    public RatingRepository(BalanceConfig config) { ArgumentNullException.ThrowIfNull(config); _config = config; }
    public PlayerRating GetOrCreate(PlayerId player) => _ratings.TryGetValue(player, out var rating) ? rating : new PlayerRating(player, 0, 0, 0);

    public (PlayerRating Winner, PlayerRating Loser) RecordOutcome(BattleOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var winner = GetOrCreate(outcome.Winner).Win(_config.RatingWinPoints);
        var loser = GetOrCreate(outcome.Loser).Lose(_config.RatingLosePoints);
        _ratings[winner.Player] = winner;
        _ratings[loser.Player] = loser;
        return (winner, loser);
    }
}
