namespace Rts.Domain;

// ---- Идентификаторы ----
public readonly record struct PlayerId(string Value);
public readonly record struct MatchId(string Value);
public readonly record struct ShipId(string Value);
public readonly record struct Team(byte Index); // 0/1 внутри матча

// ---- Лобби: очередь и матч ----

/// <summary>Состояние готовности матча.</summary>
public enum ReadyState
{
    /// <summary>Кто-то ещё не подтвердил.</summary>
    WaitingReady,

    /// <summary>Оба подтвердили — можно выбирать флоты и начинать бой.</summary>
    BothReady,

    /// <summary>Кто-то не успел за отведённое время — лобби сброшено.</summary>
    TimedOut,
}

/// <summary>Матч на двоих.</summary>
public record Match(MatchId Id, PlayerId A, PlayerId B, ReadyState State, bool ReadyA, bool ReadyB);

/// <summary>
/// Очередь лобби (кадр «Сбор матча»): два игрока → матч; ReadyUp от обоих; таймаут → сброс.
/// </summary>
public interface IMatchmakingService
{
    /// <summary>Игрок становится в очередь. Возвращает матч, если собрался.</summary>
    Match? Join(PlayerId player);

    /// <summary>Текущий матч игрока (ждущий готовности или начавшийся).</summary>
    Match? TryGetMatch(PlayerId player);

    /// <summary>Игрок подтвердил готовность.</summary>
    Match Ready(PlayerId player);

    /// <summary>Игрок отказался от матча (AnswerDuel = «нет») — лобби сбрасывается.</summary>
    void Decline(PlayerId player);

    /// <summary>
    /// Служебный такт лобби: ждущие Ready свыше баланса лобби считаются не пришедшими
    /// (событие ReadyTimeout) и их лобби сбрасывается.
    /// </summary>
    IReadOnlyList<Match> Tick(TimeSpan elapsed);
}

// ---- Флоты ----

/// <summary>Один корабль во флоте.</summary>
public record ShipUnit
{
    public ShipId Id { get; }
    public string Class { get; }
    public int MaxHull { get; }
    public int MaxShield { get; }

    public ShipUnit(ShipId id, string @class, int maxHull, int maxShield)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@class);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHull);
        ArgumentOutOfRangeException.ThrowIfNegative(maxShield);
        Id = id;
        Class = @class;
        MaxHull = maxHull;
        MaxShield = maxShield;
        Hull = maxHull;
        Shield = maxShield;
    }

    public int Hull { get; private set; }
    public int Shield { get; private set; }
    public bool Destroyed => Hull <= 0;

    internal void ApplyDamage(int damage, int shieldAbsorption)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(damage);
        ArgumentOutOfRangeException.ThrowIfNegative(shieldAbsorption);
        var absorbed = Math.Min(shieldAbsorption, Shield);
        Shield -= absorbed;
        Hull -= damage - absorbed;
    }
}

/// <summary>Флот игрока в матче.</summary>
public record Fleet(PlayerId Owner, Team Team, IReadOnlyList<ShipUnit> Ships)
{
    public bool IsWiped => Ships.All(s => s.Destroyed);
}

/// <summary>Реестр флотов (кадр «Выбор флота»).</summary>
public interface IFleetRegistry
{
    /// <summary>Выставить свой состав; можно менять до подтверждения.</summary>
    void SetFleet(MatchId match, PlayerId player, IReadOnlyList<ShipUnit> ships);

    /// <summary>Подтвердить состав (ConfirmFleet).</summary>
    void ConfirmFleet(MatchId match, PlayerId player);

    bool IsConfirmed(MatchId match, PlayerId player);
    Fleet? TryGetFleet(MatchId match, PlayerId player);
}

// ---- Баланс (все спорные числа — за одним конфигом, завтрашние правки меняют его) ----

/// <summary>Числа баланса, вынесенные из доски: правки владельца — правка этой записи.</summary>
public record BalanceConfig
{
    /// <summary>Сколько секунд ждём Ready до сброса лобби (ReadyTimeout).</summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Базовый урон выстрела за тик.</summary>
    public int BaseDamagePerTick { get; init; } = 10;

    /// <summary>Сколько урона гасит щит, пока не истощён.</summary>
    public int ShieldAbsorbsPerTick { get; init; } = 6;

    /// <summary>Очки за победу (RatingUpdated).</summary>
    public int RatingWinPoints { get; init; } = 15;

    /// <summary>Очки за поражение (минус).</summary>
    public int RatingLosePoints { get; init; } = 10;
}

// ---- Бой ----

/// <summary>Ход боя между двумя флотами.</summary>
public record Battle(MatchId Match, Fleet TeamA, Fleet TeamB)
{
    public bool IsOver => TeamA.IsWiped || TeamB.IsWiped;
}

/// <summary>Итог тика боя.</summary>
/// <summary>Итог тика боя: урон ПО КОРПУСУ каждой стороны (после гашения щитами).</summary>
public record BattleTickResult(Battle Battle, int HullDamageToTeamA, int HullDamageToTeamB);

/// <summary>Решатель урона тика (кадры «Бой», числа — из BalanceConfig).</summary>
public interface ICombatService
{
    Battle Start(MatchId match, Fleet teamA, Fleet teamB);

    /// <summary>Один тик: живые корабли команды A наносят урон команде B и наоборот.</summary>
    BattleTickResult Tick(Battle battle);
}

// ---- Сборка матча в бой (кадр 3b → 4b) ----

/// <summary>
/// Событие старта матча: оба флота подтверждены (AllFleetsConfirmed) после BothReady,
/// бой собран через ICombatService.Start.
/// </summary>
public record MatchStarted(MatchId Match, PlayerId PlayerA, PlayerId PlayerB, Battle Battle);

/// <summary>
/// Тонкий слой сборки матча в бой: сшивает лобби, реестр флотов и бой.
/// Когда лобби в BothReady и оба флота подтверждены — стартует бой (MatchStarted).
/// </summary>
public interface IMatchOrchestrator
{
    /// <summary>Прикрепить матч лобби: слой запоминает его игроков (A и B).</summary>
    void Attach(Match match);

    /// <summary>Выставить состав флота (делегируется в IFleetRegistry).</summary>
    void SetFleet(MatchId match, PlayerId player, IReadOnlyList<ShipUnit> ships);

    /// <summary>
    /// Подтвердить состав (ConfirmFleet, делегируется в IFleetRegistry). Если этим подтверждением
    /// закрыт последний неподтверждённый флот и лобби в BothReady — матч стартует.
    /// </summary>
    MatchStarted? ConfirmFleet(MatchId match, PlayerId player);

    /// <summary>
    /// Проверить условие старта вручную (AllFleetsConfirmed при BothReady) — например, после
    /// того как лобби перешло в BothReady, когда флоты уже подтверждены.
    /// </summary>
    MatchStarted? TryStartBattle(MatchId match);

    /// <summary>Все ли флоты матча подтверждены (AllFleetsConfirmed).</summary>
    bool AllFleetsConfirmed(MatchId match);

    /// <summary>Бой матча, если он уже стартовал.</summary>
    Battle? TryGetBattle(MatchId match);
}

// ---- Рейтинг и статистика ----

/// <summary>Профиль рейтинга игрока.</summary>
public record PlayerRating(PlayerId Player, int Points, int Wins, int Losses);

/// <summary>Итог боя для рейтинга (BattleEnded).</summary>
public record BattleOutcome(PlayerId Winner, PlayerId Loser);

/// <summary>
/// Хранилище рейтинга (кадр «Итоги»): после боя победителю +points, проигравшему −points,
/// счётчики побед/поражений растут (StatsRecorded).
/// </summary>
public interface IRatingRepository
{
    PlayerRating GetOrCreate(PlayerId player);

    /// <summary>Применить исход боя; возвращает обновлённые профили [победитель, проигравший].</summary>
    (PlayerRating Winner, PlayerRating Loser) RecordOutcome(BattleOutcome outcome);
}

// ---- Хост-фасад игры (сборка домена под тест/вышестоящий слой) ----

/// <summary>Фабрика домен-сервисов: собрать инфраструктуру над абстракциями.</summary>
public interface IDomainFactory
{
    IMatchmakingService CreateLobby(BalanceConfig config);
    IFleetRegistry CreateFleetRegistry();
    ICombatService CreateCombat(BalanceConfig config);
    IRatingRepository CreateRating(BalanceConfig config);
    IMatchOrchestrator CreateOrchestrator(IMatchmakingService lobby, IFleetRegistry fleets, ICombatService combat);
}