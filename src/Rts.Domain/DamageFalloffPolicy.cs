namespace Rts.Domain;

// ---- P11: урон от дистанции (спад как необязательная политика; дефолт null = прежнее поведение) ----

/// <summary>
/// Числа спада урона от дистанции (P11). ДЕФОЛТЫ по аналогии со стандартными RTS, НЕ подтверждены
/// владельцем; правятся правкой этой записи.
///
/// Формула множителя урона выстрела по дистанции d (стрелок → цель):
/// — d ≤ CloseRange             → 1.0 (урон полный);
/// — CloseRange &lt; d &lt; WeaponRange → линейный спад от 1.0 к FarFloor:
///       mult(d) = FarFloor + (1 − FarFloor) × (WeaponRange − d) / (WeaponRange − CloseRange);
/// — d ≥ WeaponRange            → FarFloor (дальше триггера боя лейн и не тикает).
/// </summary>
public sealed record DamageFalloffConfig
{
    /// <summary>Заключительная «близкая» дистанция: до неё урон полный. Дефолт (150), не подтверждено.</summary>
    public const double DefaultCloseRange = 150;

    /// <summary>Пол пола спада на границе WeaponRange. Дефолт (0.4), не подтверждено.</summary>
    public const double DefaultFarFloor = 0.4;

    public double CloseRange { get; init; } = DefaultCloseRange; // дефолт (150), не подтверждено

    public double FarFloor { get; init; } = DefaultFarFloor;     // дефолт (0.4), не подтверждено

    public static DamageFalloffConfig Default() => new();
}

/// <summary>
/// Политика спада урона от дистанции (P11) как IShotDamageModifier: множитель выстрела стрелок→цель
/// берётся по дистанции из позиций живых кораблей (позиции приходят извне — в сессии это
/// позиции сервиса движения, дальность триггера — из tuning сессии). Подключается
/// необязательным конфигом; дефолт null = прежнее поведение целиком.
/// </summary>
public sealed class DamageFalloffPolicy : IShotDamageModifier
{
    private readonly Func<MoveKey, ShipPosition> _positionOf;
    private readonly double _weaponRange;
    private readonly DamageFalloffConfig _config;

    public DamageFalloffPolicy(Func<MoveKey, ShipPosition> positionOf, double weaponRange, DamageFalloffConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(positionOf);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weaponRange);
        _positionOf = positionOf;
        _weaponRange = weaponRange;
        _config = config ?? DamageFalloffConfig.Default();
    }

    public double ShotMultiplierOf(PlayerId attacker, ShipUnit attackerShip, PlayerId target, ShipUnit targetShip)
    {
        var distance = _positionOf(new MoveKey(attacker, attackerShip.Id))
            .DistanceTo(_positionOf(new MoveKey(target, targetShip.Id)));
        if (distance <= _config.CloseRange)
        {
            return 1.0; // «близко»: урон полный
        }

        if (distance >= _weaponRange)
        {
            return _config.FarFloor; // дальше триггера боя тик и не случится — на всякий случай пол
        }

        // линейный спад между CloseRange (1.0) и WeaponRange (FarFloor)
        return _config.FarFloor + (1.0 - _config.FarFloor) * (_weaponRange - distance) / (_weaponRange - _config.CloseRange);
    }
}
