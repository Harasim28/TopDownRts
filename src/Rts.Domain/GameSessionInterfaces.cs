namespace Rts.Domain;

// ---- Полный игровой ход (интеграция кадров 1 + 3a/3b + 4a/4b + 5) ----

/// <summary>Исход сессии: ещё не решён / победа команды A / победа команды B / ничья.</summary>
public enum SessionOutcome
{
    /// <summary>Ход не завершён.</summary>
    None,

    /// <summary>Флоты стороны B уничтожены либо победа A по базам.</summary>
    TeamAWon,

    /// <summary>Флоты стороны A уничтожены либо победа B по базам.</summary>
    TeamBWon,

    /// <summary>Обе стороны уничтожены в один тик или счёт лэйнов 1:1 — «одинаковое уничтожение».</summary>
    Draw,
}

/// <summary>Срез корабля для клиента: кто, где, сколько топлива и корпуса (HP видно клиенту при лечении), жив ли.</summary>
public record SessionShipState(PlayerId Player, Team Team, ShipId Ship, string Class, ShipPosition Position, double Fuel, bool Alive, int Hull);

/// <summary>Срез базы для клиента: владелец, кто захватывает и прогресс.</summary>
public record SessionBaseState(BaseId Base, Team? Owner, Team? Capturing, double CaptureProgressSeconds);

/// <summary>Снимок полного хода (GetState — всё нужное будущему клиенту в одном разе).</summary>
public record GameSessionState(
    SessionOutcome Outcome,
    bool IsOver,
    bool BattleStarted,
    Team? Winner,
    IReadOnlyList<SessionShipState> Ships,
    IReadOnlyList<SessionBaseState> Bases,
    double ScoreTeamA,
    double ScoreTeamB,
    double HoldAllSecondsTeamA,
    double HoldAllSecondsTeamB);

/// <summary>
/// Числа интеграции полного хода (не режимные, не балансные): триггер боя по дальности и рассадка
/// спавна. Все — ДЕФОЛТЫ по аналогии со стандартными RTS/WarThunder, НЕ подтверждены владельцем,
/// правятся правкой этой записи.
/// </summary>
public record GameSessionTuning
{
    /// <summary>
    /// Дальность триггера боя, юнитов: тик боя лэйна выполняется, только если живые корабли его
    /// сторон сблизились ближе этого значения (базы не воюют). Дефолт (300), не подтверждено.
    /// </summary>
    public const double DefaultWeaponRange = 300;

    /// <summary>Сдвиг спавна игрока от точки базы, юнитов: (слот − 0.5) × 2 × шаг. Дефолт (30), не подтверждено.</summary>
    public const double DefaultSpawnOffsetPerPlayer = 30;

    /// <summary>Разброс кораблей одного игрока у точки спавна, юнитов: (k − (n−1)/2) × шаг. Дефолт (30), не подтверждено.</summary>
    public const double DefaultSpawnSpreadPerShip = 30;

    public double WeaponRange { get; init; } = DefaultWeaponRange; // дефолт (300), не подтверждено

    public double SpawnOffsetPerPlayer { get; init; } = DefaultSpawnOffsetPerPlayer; // дефолт (30)

    public double SpawnSpreadPerShip { get; init; } = DefaultSpawnSpreadPerShip; // дефолт (30)

    public static GameSessionTuning Default() => new();
}

/// <summary>
/// Фасад полного игрового хода 2v2 (базовая рабочая версия). Сборка: участники комнаты в порядке
/// очередности делятся пополам (первые двое — команда A, затем команда B); лобби-матчи сходятся
/// крест-накрест A(0)↔B(0) и A(1)↔B(1); закуп и подтверждение флотов идут через существующий
/// IMatchOrchestrator (капа закупа — в IFleetRegistry); MatchStarted лэйна — сессия спавнит его
/// корабли у HomeA/HomeB. Тик полного хода: движение всех живых → присутствие в зонах баз →
/// такт режима (захват/очки/таймер) → такты боев лэйнов (по триггеру дальности) → проверка исхода.
/// После исхода сессия замерзает: тики и команды ничего не меняют, результат уже в рейтинге.
/// </summary>
public interface IGameSession
{
    RoomId Room { get; }

    /// <summary>Правила режима, заданные хостом комнаты (слайдеры) — работают как есть.</summary>
    HostRuleSet Rules { get; }

    /// <summary>Игроки команды A (первые двое участников комнаты).</summary>
    IReadOnlyList<PlayerId> TeamAPlayers { get; }

    /// <summary>Игроки команды B (последние двое участников комнаты).</summary>
    IReadOnlyList<PlayerId> TeamBPlayers { get; }

    /// <summary>Лобби 2v2: два матча крест-накрест (видно BothReady и составы пар).</summary>
    IMatchmakingService Lobby { get; }

    /// <summary>Репозиторий рейтинга, куда ушёл исход (одна запись на пару победитель↔проигравший).</summary>
    IRatingRepository Ratings { get; }

    bool IsOver { get; }

    SessionOutcome Outcome { get; }

    /// <summary>Команда-победитель (null при ничьей, пока не решено).</summary>
    Team? Winner { get; }

    /// <summary>Выставить состав флота игрока (капа закупа на игрока из BalanceConfig — ошибку отдаёт реестр).</summary>
    void SetFleet(PlayerId player, IReadOnlyList<ShipUnit> ships);

    /// <summary>
    /// Подтвердить состав. Вернёт MatchStarted, если этим подтверждением закрылся флоты пары лэйна —
    /// сессия тогда спавнит его корабли у баз. До этого null.
    /// </summary>
    MatchStarted? ConfirmFleet(PlayerId player);

    /// <summary>Задать путь кораблю игрока (после спавна: корабли известны парой (игрок, ShipId)).</summary>
    void SetPath(PlayerId player, ShipId ship, IReadOnlyList<Waypoint> path);

    /// <summary>Один тик полного хода; после исхода — ничего (сессия замерзает).</summary>
    void Tick(TimeSpan span);

    /// <summary>Срез состояния: позиции/топливо/живость кораблей, базы, очки, hold-секунды, исход.</summary>
    GameSessionState GetState();
}
