namespace Rts.Domain;

// ---- Кадр 4a «Карта и движение флота» (интеграция полного хода) ----

/// <summary>Точка на карте 2000x2000 (минимум векторной математики над координатами).</summary>
public readonly record struct ShipPosition(double X, double Y)
{
    public static ShipPosition Zero { get; } = new(0, 0);

    public static ShipPosition operator +(ShipPosition p, ShipPosition v) => new(p.X + v.X, p.Y + v.Y);
    public static ShipPosition operator -(ShipPosition p, ShipPosition v) => new(p.X - v.X, p.Y - v.Y);

    public ShipPosition Scaled(double factor) => new(X * factor, Y * factor);

    /// <summary>Длина вектора (расстояние от нуля).</summary>
    public double Length() => Math.Sqrt(X * X + Y * Y);

    /// <summary>Расстояние до другой точки.</summary>
    public double DistanceTo(ShipPosition other) => (other - this).Length();

    public ShipPosition StepToward(ShipPosition target, double step) =>
        step >= DistanceTo(target) ? target : this + DirectionTo(target).Scaled(step);

    /// <summary>Единичный вектор по направлению на target; нулевой, если совпадаем с target.</summary>
    public ShipPosition DirectionTo(ShipPosition target)
    {
        var delta = target - this;
        var length = delta.Length();
        return length <= 0 ? Zero : delta.Scaled(1 / length);
    }
}

/// <summary>
/// Точка маршрута (команда пути). Движение идёт к точке со скоростью из MovementConfig;
/// <see cref="Afterburner"/> включает форсаж на участке к этой точке (x2 скорость, расход топлива).
/// </summary>
public record Waypoint(ShipPosition Point, bool Afterburner = false);

/// <summary>
/// Ключ движущегося корабля: владелец (игрок сессии) + корабль. ShipId могут совпасть
/// у разных игроков (составы независимы), поэтому ключ — пара (игрок, корабль).
/// </summary>
public readonly record struct MoveKey(PlayerId Owner, ShipId Ship);

/// <summary>
/// Числа карты и движения (кадр 4a), ДЕФОЛТЫ по аналогии со стандартными RTS (WarThunder):
/// НЕ подтверждены владельцем, правятся правкой этой записи — одной строкой в одном месте.
/// </summary>
public record MovementConfig
{
    /// <summary>Ширина карты, юнитов. Дефолт (2000), не подтверждено.</summary>
    public const double DefaultMapWidth = 2000;

    /// <summary>Высота карты, юнитов. Дефолт (2000), не подтверждено.</summary>
    public const double DefaultMapHeight = 2000;

    /// <summary>Радиус зоны базы, юнитов. Дефолт (150), не подтверждено.</summary>
    public const double DefaultBaseZoneRadius = 150;

    /// <summary>Базовая скорость, юнитов/сек. Дефолт (100), не подтверждено.</summary>
    public const double DefaultBaseSpeedUnitsPerSecond = 100;

    /// <summary>Множитель скорости для класса вне словаря. Дефолт (1.0), не подтверждено.</summary>
    public const double DefaultUnknownClassSpeedMultiplier = 1.0;

    /// <summary>Форсаж: множитель скорости x2. Дефолт (2.0), не подтверждено.</summary>
    public const double DefaultAfterburnerSpeedMultiplier = 2.0;

    /// <summary>Форсаж: расход топлива, ед/сек. Дефолт (10), не подтверждено.</summary>
    public const double DefaultAfterburnerFuelPerSecond = 10;

    /// <summary>Бак: ёмкость; спавн с полным баком. Дефолт (100), не подтверждено.</summary>
    public const double DefaultFuelCapacity = 100;

    /// <summary>Регенерация топлива, ед/сек, в обычном ходу (в форсаже — нет). Дефолт (2), не подтверждено.</summary>
    public const double DefaultFuelRegenPerSecond = 2;

    public double MapWidth { get; init; } = DefaultMapWidth;   // дефолт, не подтверждено
    public double MapHeight { get; init; } = DefaultMapHeight; // дефолт, не подтверждено

    /// <summary>
    /// Точки трёх баз: дом A (400,1000), центр (1000,1000), дом B (1600,1000).
    /// Дефолт, не подтверждено.
    /// </summary>
    public IReadOnlyDictionary<BaseId, ShipPosition> BasePoints { get; init; } = DefaultBasePoints;

    public static IReadOnlyDictionary<BaseId, ShipPosition> DefaultBasePoints { get; } =
        new Dictionary<BaseId, ShipPosition>
        {
            [BaseId.HomeA] = new(400, 1000),
            [BaseId.Central] = new(1000, 1000),
            [BaseId.HomeB] = new(1600, 1000),
        };

    public double BaseZoneRadius { get; init; } = DefaultBaseZoneRadius; // дефолт (150), не подтверждено

    public double BaseSpeedUnitsPerSecond { get; init; } = DefaultBaseSpeedUnitsPerSecond; // дефолт (100)

    /// <summary>
    /// Множители скорости по классу: scout 1.8, light 1.4, medium 1.0, heavy 0.7,
    /// support 1.0, supply 0.9. Дефолты, НЕ подтверждены.
    /// </summary>
    public IReadOnlyDictionary<string, double> ClassSpeedMultipliers { get; init; } = DefaultClassSpeedMultipliers;

    public static IReadOnlyDictionary<string, double> DefaultClassSpeedMultipliers { get; } =
        new Dictionary<string, double>
        {
            ["scout"] = 1.8,   // дефолт, не подтверждено
            ["light"] = 1.4,   // дефолт, не подтверждено
            ["medium"] = 1.0,  // дефолт, не подтверждено
            ["heavy"] = 0.7,   // дефолт, не подтверждено
            ["support"] = 1.0, // дефолт, не подтверждено
            ["supply"] = 0.9,  // дефолт, не подтверждено
        };

    public double UnknownClassSpeedMultiplier { get; init; } = DefaultUnknownClassSpeedMultiplier;

    public double AfterburnerSpeedMultiplier { get; init; } = DefaultAfterburnerSpeedMultiplier; // дефолт (x2)

    public double AfterburnerFuelPerSecond { get; init; } = DefaultAfterburnerFuelPerSecond; // дефолт (10/с)

    public double FuelCapacity { get; init; } = DefaultFuelCapacity; // дефолт (100), не подтверждено

    public double FuelRegenPerSecond { get; init; } = DefaultFuelRegenPerSecond; // дефолт (2/с), не подтверждено

    /// <summary>Множитель скорости класса: известный класс — словарь, иной — UnknownClassSpeedMultiplier.</summary>
    public double SpeedMultiplierOf(string @class) =>
        ClassSpeedMultipliers.TryGetValue(@class, out var multiplier) ? multiplier : UnknownClassSpeedMultiplier;

    public static MovementConfig Default() => new();
}

/// <summary>
/// Сервис движения флотов (кадр 4a): путь из путевых точек, скорость из MovementConfig,
/// форсаж и топливо. Поворот и коллизии каркасом НЕ делаются (осознанное упрощение).
/// Всё тиками: Tick(dt), никакого реального времени.
/// </summary>
public interface IFleetMovementService
{
    /// <summary>Зарегистрировать корабль на карте (бак полон). Повторная регистрация — ошибка.</summary>
    void RegisterShip(MoveKey key, string shipClass, ShipPosition at);

    /// <summary>Задать путь (путевые точки); путь заменяется целиком, пустой — стоп.</summary>
    void SetPath(MoveKey key, IReadOnlyList<Waypoint> path);

    /// <summary>Один тик движения: двигает к первой точке пути; доехал до точки — стоит.</summary>
    void Tick(TimeSpan span);

    ShipPosition PositionOf(MoveKey key);

    /// <summary>Остаток топлива в [0..FuelCapacity].</summary>
    double FuelOf(MoveKey key);

    /// <summary>Едет ли корабль (путь непустой и последняя точка не достигнута).</summary>
    bool IsMoving(MoveKey key);
}

/// <summary>Карта баз: точки баз из MovementConfig и зоны-круги радиуса BaseZoneRadius.</summary>
public interface IBaseMap
{
    IReadOnlyDictionary<BaseId, ShipPosition> BasePoints { get; }

    double ZoneRadius { get; }

    ShipPosition CenterOf(BaseId baseId);

    /// <summary>База, чья зона накрывает точку, иначе null (на дефолт-карте зоны не пересекаются).</summary>
    BaseId? TryZoneOf(ShipPosition point);
}
