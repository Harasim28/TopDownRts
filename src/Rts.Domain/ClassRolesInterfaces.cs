namespace Rts.Domain;

// ---- Спец-роли классов (support/supply/scout) + аддитивный крючок урона выстрела ----

/// <summary>
/// Числа спец-ролей классов. Все — ДЕФОЛТЫ по аналогии со стандартными RTS, НЕ подтверждены
/// владельцем; правятся правкой этой записи (тесты задают свой конфиг).
/// </summary>
public sealed record ClassRolesConfig
{
    /// <summary>Радиус аур (леченье/дозаправка), юнитов. Дефолт (300), не подтверждено.</summary>
    public const double DefaultAuraRadius = 300;

    /// <summary>Лечение корпуса support-ом, ед/сек живым союзникам (включая себя). Дефолт (+5), не подтверждено.</summary>
    public const double DefaultSupportHealPerSecond = 5;

    /// <summary>Дозаправка supply-ом, ед топлива/сек СВЕРХ регенерации. Дефолт (+5), не подтверждено.</summary>
    public const double DefaultSupplyFuelPerSecond = 5;

    /// <summary>Множитель ПОЛУЧАЕМОГО scout-ом урона (уклонение). Дефолт (0.7), не подтверждено.</summary>
    public const double DefaultScoutDamageMultiplier = 0.7;

    // Классы-носители ролей: строка — как в BalanceConfig.ShipClasses; дефолт «как назван в конфиге».
    public const string DefaultSupportClass = "support";
    public const string DefaultSupplyClass = "supply";
    public const string DefaultScoutClass = "scout";

    public double SupportRadius { get; init; } = DefaultAuraRadius;    // дефолт (300), не подтверждено

    public double SupplyRadius { get; init; } = DefaultAuraRadius;     // дефолт (300), не подтверждено

    public double SupportHealPerSecond { get; init; } = DefaultSupportHealPerSecond; // дефолт (+5)

    public double SupplyFuelPerSecond { get; init; } = DefaultSupplyFuelPerSecond;   // дефолт (+5)

    public double ScoutDamageMultiplier { get; init; } = DefaultScoutDamageMultiplier; // дефолт (0.7)

    public string SupportClass { get; init; } = DefaultSupportClass;
    public string SupplyClass { get; init; } = DefaultSupplyClass;
    public string ScoutClass { get; init; } = DefaultScoutClass;

    public static ClassRolesConfig Default() => new();
}

/// <summary>Снимок одного корабля для такта спец-ролей: команда/класс/живость плюс живые числа.</summary>
public sealed record ClassRolesUnit(
    PlayerId Player, Team Team, ShipId Ship, string ShipClass, ShipPosition Position, bool Alive,
    int Hull, int MaxHull, double Fuel, double FuelCapacity);

/// <summary>Такт спец-ролей: длительность и снимок всех спавненных кораблей сессии (после движения).</summary>
public sealed record ClassRolesTick(TimeSpan Span, IReadOnlyList<ClassRolesUnit> Units);

/// <summary>Эффект такта по паре (владелец, корабль): сколько корпуса долечено и топлива долито.</summary>
public sealed record ClassRolesDelta(PlayerId Player, ShipId Ship, int HullHealed, double FuelAdded);

/// <summary>Результат такта спец-ролей.</summary>
public sealed record ClassRolesTickResult(IReadOnlyList<ClassRolesDelta> Applied);

/// <summary>
/// Политика спец-ролей классов: ауры лечать/дозаправлять живых СОЮЗНИКОВ (включая носителя),
/// уклонение scout — через IShotDamageModifier. Сессия вызывает Tick после движения.
/// </summary>
public interface IClassRolesPolicy
{
    ClassRolesTickResult Tick(ClassRolesTick tick);
}

// ---- Аддитивный крючок урона выстрела (уклонение scout, позже P11-спад) ----

/// <summary>
/// Модификатор урона ОДИНОЧНОГО выстрела «стрелок → цель» (1.0 = без изменений). Политики:
/// уклонение scout (класс цели), спад урона от дистанции (P11). CombatService перемножает
/// множители по каждому живому стрелку и только потом гасит щитом.
/// </summary>
public interface IShotDamageModifier
{
    double ShotMultiplierOf(PlayerId attacker, ShipUnit attackerShip, PlayerId target, ShipUnit targetShip);
}

/// <summary>
/// Аддитивный крючок такта боя: подключаемый модификатор урона выстрела; null = прежнее поведение.
/// ICombatService остаётся нетронутым — существующие вызовы и фейковые реализации не ломаются.
/// </summary>
public interface ICombatShotTuning
{
    void SetShotModifier(IShotDamageModifier? modifier);
}

/// <summary>Комбинация модификаторов урона выстрела: множители ПЕРЕМНОЖАЮТСЯ (спад × уклонение).</summary>
public sealed class AggregateShotModifier : IShotDamageModifier
{
    private readonly IReadOnlyList<IShotDamageModifier> _modifiers;

    public AggregateShotModifier(IReadOnlyList<IShotDamageModifier> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        _modifiers = modifiers;
    }

    /// <summary>Сшить политики: пусто — null (прежнее поведение), одна — она же, иначе — комбинация.</summary>
    public static IShotDamageModifier? Combine(IReadOnlyList<IShotDamageModifier> modifiers) => modifiers.Count switch
    {
        0 => null,
        1 => modifiers[0],
        _ => new AggregateShotModifier(modifiers),
    };

    public double ShotMultiplierOf(PlayerId attacker, ShipUnit attackerShip, PlayerId target, ShipUnit targetShip) =>
        _modifiers.Aggregate(1.0, (multiplier, modifier) => multiplier * modifier.ShotMultiplierOf(attacker, attackerShip, target, targetShip));
}
