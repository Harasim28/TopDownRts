namespace Rts.Domain;

// ---- Рейтинговая политика (кадр «Итоги», P15): подключаемая схема счёта после исхода ----
// Дефолт сессий — прежняя плоская схема IRatingRepository (±points); политика подключается
// необязательным параметром при создании сессии и ничего не меняет у существующих вызовов.

/// <summary>Кто победил в игре, к которой применена рейтинговая политика.</summary>
public enum RatingGameWinner
{
    TeamA,
    TeamB,

    /// <summary>Ничья: счёт S стороны задаёт конфиг политики (шахматное Эло — 0.5).</summary>
    Draw,
}

/// <summary>
/// Игра для рейтинга: составы сторон по порядку и исход. Составы важны — политика с командным
/// средним (шахматное Эло 2v2) считает средний рейтинг ПРОТИВНИКОВ по всей противной стороне.
/// </summary>
public record RatingGameResult(
    IReadOnlyList<PlayerId> TeamA,
    IReadOnlyList<PlayerId> TeamB,
    RatingGameWinner Winner);

/// <summary>
/// Политика рейтинга: после решённого исхода пересчитывает рейтинги ВСЕХ игроков обеих сторон
/// (плоской схеме +N/−N политика не нужна — она живёт в IRatingRepository «как есть»).
/// </summary>
public interface IRatingPolicy
{
    /// <summary>Стартовый рейтинг игрока без сыгранных игр (репозиторий политики берёт его для GetOrCreate).</summary>
    int StartRating { get; }

    /// <summary>
    /// Применить исход к текущим рейтингам игроков; вернуть обновлённые профили всех игроков
    /// исхода (обеих сторон). Рейтинги без записи в словаре считаются стартовыми.
    /// </summary>
    IReadOnlyList<PlayerRating> Apply(RatingGameResult result, IReadOnlyDictionary<PlayerId, PlayerRating> ratings);
}

/// <summary>
/// Репозиторий рейтинга с подключаемой политикой: хранит профили сам, после Apply сохраняет
/// обновления. Плоская схема (RatingRepository) этот контракт не требует и не реализует.
/// </summary>
public interface IPolicyRatingRepository : IRatingRepository
{
    /// <summary>Собрать текущие профили участников исхода, применить политику и сохранить результат.</summary>
    IReadOnlyList<PlayerRating> Apply(RatingGameResult result);
}

/// <summary>
/// Конфиг шахматного Эло (FIDE). Все числа — дефолты, НЕ подтверждены владельцем; ступени K и
/// пороги вынесены сюда, чтобы перезаписать лестницу под ICCup правкой одной этой записи.
/// </summary>
public sealed record EloConfig
{
    /// <summary>
    /// Стартовый рейтинг новичка. Дефолт: фактически используемый старт RatingRepository —
    /// GetOrCreate создаёт профиль с Points = 0 (старт в коде есть, поэтому не 1000);
    /// НЕ подтверждено владельцем.
    /// </summary>
    public int StartRating { get; init; } = 0;

    /// <summary>D — разброс логистической кривой ожидаемого результата. Дефолт: 400, классика FIDE.</summary>
    public int D { get; init; } = 400;

    /// <summary>K новичка: пока rated-игр меньше NoviceGamesThreshold. Дефолт: 40 (FIDE).</summary>
    public int NoviceK { get; init; } = 40;

    /// <summary>K основного диапазона: по числу игр вырос из новичков, рейтинг ниже мастера. Дефолт: 20 (FIDE).</summary>
    public int NormalK { get; init; } = 20;

    /// <summary>K мастера: рейтинг от MasterRatingThreshold и выше. Дефолт: 10 (FIDE).</summary>
    public int MasterK { get; init; } = 10;

    /// <summary>Порог новичка по числу rated-игр (Wins + Losses до текущего исхода). Дефолт: 30 (FIDE).</summary>
    public int NoviceGamesThreshold { get; init; } = 30;

    /// <summary>Порог мастера по рейтингу: с него K падает до MasterK. Дефолт: 2400 (FIDE).</summary>
    public int MasterRatingThreshold { get; init; } = 2400;

    /// <summary>Очки S за ничью (S победы = 1, поражения = 0). Дефолт: 0.5, шахматное Эло FIDE.</summary>
    public double DrawScore { get; init; } = 0.5;
}
