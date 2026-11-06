namespace Rts.Domain;

/// <summary>
/// Шахматное Эло (FIDE) как IRatingPolicy: для каждого игрока исхода
/// E_i = 1/(1+10^((R_oppTeamAvg_i − R_i)/D)), R'_i = R_i + K_i×(S_i − E_i),
/// где R_oppTeamAvg — средний рейтинг ВСЕХ противников другой стороны (в 2v2 — двух),
/// S = 1 / 0 / EloConfig.DrawScore (победа / поражение / ничья), K_i — по числу rated-игр
/// (Wins + Losses ДО этого исхода) и текущему рейтингу: ступени из EloConfig
/// (дефолт FIDE: 40 у новичков, 20 в основном диапазоне, 10 с 2400). Дельта округляется
/// классически к целому (0.5 — вверх): PlayerRating.Points целочисленный. Счётчики
/// Wins/Losses политика ведёт сама: победа +1 Win, поражение +1 Loss, ничья — без счётчика.
/// Конфиг по умолчанию — новый EloConfig(); под ICCup-лестницу правится одна запись конфига.
/// </summary>
public sealed class EloPolicy : IRatingPolicy
{
    private readonly EloConfig _config;

    public EloPolicy(EloConfig? config = null) => _config = config ?? new EloConfig();

    /// <summary>Конфиг ступеней и порогов (правка под ICCup — правка конфига политики).</summary>
    public EloConfig Config => _config;

    /// <summary>Стартовый рейтинг новичков — из конфига (дефолт 0, как у RatingRepository).</summary>
    public int StartRating => _config.StartRating;

    public IReadOnlyList<PlayerRating> Apply(RatingGameResult result, IReadOnlyDictionary<PlayerId, PlayerRating> ratings)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(ratings);

        // профиль без записи в словаре — стартовый игрок этой политики
        PlayerRating CurrentOf(PlayerId player) =>
            ratings.TryGetValue(player, out var rating)
                ? rating
                : new PlayerRating(player, _config.StartRating, 0, 0);

        var updated = new List<PlayerRating>();
        // обе стороны считаем одной логикой: у игрока противники — вся противная сторона
        foreach (var (team, opponents) in new[] { (RatingGameWinner.TeamA, result.TeamB), (RatingGameWinner.TeamB, result.TeamA) })
        {
            var score = ScoreOf(result.Winner, team); // 1 / 0 / DrawScore (ничья — обоим)
            var isWin = result.Winner == team;
            var isLoss = result.Winner is not RatingGameWinner.Draw && !isWin;
            foreach (var player in TeamPlayers(result, team))
            {
                var rating = CurrentOf(player);
                var opponentAverage = opponents.Count == 0
                    ? rating.Points // вырожденный исход без противников: противник «сам с собой» (E = 0.5)
                    : opponents.Average(opponent => CurrentOf(opponent).Points);
                var expected = Expected(rating.Points, opponentAverage);
                var step = Step(rating, score - expected);

                updated.Add(new PlayerRating(
                    player,
                    rating.Points + step,
                    rating.Wins + (isWin ? 1 : 0),
                    rating.Losses + (isLoss ? 1 : 0)));
            }
        }

        return updated;
    }

    private IReadOnlyList<PlayerId> TeamPlayers(RatingGameResult result, RatingGameWinner team) =>
        team == RatingGameWinner.TeamA ? result.TeamA : result.TeamB;

    /// <summary>Очки S стороны: победа 1, поражение 0, ничья — DrawScore из конфига.</summary>
    private double ScoreOf(RatingGameWinner winner, RatingGameWinner team) =>
        winner == RatingGameWinner.Draw ? _config.DrawScore : winner == team ? 1.0 : 0.0;

    /// <summary>Ожидаемый результат по классической кривой Эло с разбросом D.</summary>
    private double Expected(int points, double opponentAverage) =>
        1.0 / (1.0 + Math.Pow(10, (opponentAverage - points) / (double)_config.D));

    /// <summary>Шаг K_i по ступеням конфига: <30 rated-игр — NoviceK, рейтинг <2400 — NormalK, далее MasterK.</summary>
    private int Step(PlayerRating rating, double delta)
    {
        var games = rating.Wins + rating.Losses;
        var k = games < _config.NoviceGamesThreshold
            ? _config.NoviceK
            : rating.Points < _config.MasterRatingThreshold ? _config.NormalK : _config.MasterK;
        return (int)Math.Round(k * delta, MidpointRounding.AwayFromZero); // классическое округление к ближайшему
    }
}

/// <summary>
/// Хранилище рейтинга с подключённой политикой (IPolicyRatingRepository): новые игроки получают
/// старт политики (IRatingPolicy.StartRating), Apply применяет политике и сохраняет профили.
/// RecordOutcome трактует пару победитель↔проигравший как дуэль 1v1 по той же политике —
/// плоская схема ±15/−10 остаётся в RatingRepository (дефолт сессий).
/// </summary>
public sealed class EloRatingRepository : IPolicyRatingRepository
{
    private readonly IRatingPolicy _policy;
    private readonly Dictionary<PlayerId, PlayerRating> _ratings = new();

    public EloRatingRepository(IRatingPolicy policy) =>
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public PlayerRating GetOrCreate(PlayerId player) =>
        _ratings.TryGetValue(player, out var rating) ? rating : new PlayerRating(player, _policy.StartRating, 0, 0);

    public IReadOnlyList<PlayerRating> Apply(RatingGameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var current = result.TeamA.Concat(result.TeamB)
            .Distinct()
            .ToDictionary(player => player, GetOrCreate);
        var updated = _policy.Apply(result, current);
        foreach (var rating in updated)
        {
            _ratings[rating.Player] = rating;
        }

        return updated;
    }

    public (PlayerRating Winner, PlayerRating Loser) RecordOutcome(BattleOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var updated = Apply(new RatingGameResult([outcome.Winner], [outcome.Loser], RatingGameWinner.TeamA));
        return (updated[0], updated[1]);
    }
}
