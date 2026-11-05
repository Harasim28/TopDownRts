namespace Rts.Domain;

/// <summary>Карта баз по MovementConfig: точки из конфига, зоны — круги радиуса BaseZoneRadius.</summary>
public sealed class BattleMap : IBaseMap
{
    private readonly IReadOnlyDictionary<BaseId, ShipPosition> _points;

    public BattleMap(MovementConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _points = config.BasePoints;
        ZoneRadius = config.BaseZoneRadius;
    }

    public IReadOnlyDictionary<BaseId, ShipPosition> BasePoints => _points;

    public double ZoneRadius { get; }

    public ShipPosition CenterOf(BaseId baseId) =>
        _points.TryGetValue(baseId, out var point)
            ? point
            : throw new ArgumentOutOfRangeException(nameof(baseId), baseId, "Неизвестная база карты");

    public BaseId? TryZoneOf(ShipPosition point)
    {
        foreach (var (baseId, center) in _points)
        {
            if (point.DistanceTo(center) <= ZoneRadius)
            {
                return baseId;
            }
        }

        return null;
    }
}

/// <summary>
/// Реестр движений in-memory (кадр 4a). Скорость = базовая × множитель класса × (форсаж? x2 : 1).
/// Топливо: форсаж жжёт AfterburnerFuelPerSecond за секунду форсажа; в обычном ходу (едем без форсажа
/// или стоим) топливо регенерирует FuelRegenPerSecond, выше ёмкости бака — не переливается.
/// Дефолт-трактовки (не подтверждены владельцем):
/// — решение о форсаже принимается на участке, где бак не пуст в начале отрезка тика;
///   если бак опустел посреди участка — до конца этого участка скорость падает к базовой,
///   форсаж на нём же не включается снова (следующий участок решает заново);
/// — позиции не ограничиваются краями карты (края — забота клиента);
/// — «доехал до точки — стоит» с продолжающейся регенерацией топлива.
/// </summary>
public sealed class FleetMovementService : IFleetMovementService
{
    private sealed class Unit
    {
        public required string ShipClass;
        public ShipPosition Position;
        public double Fuel;
        public double ClassMultiplier;
        public Queue<Waypoint> Path = new();
    }

    private readonly MovementConfig _config;
    private readonly Dictionary<MoveKey, Unit> _units = new();

    public FleetMovementService(MovementConfig? config = null)
    {
        _config = config ?? MovementConfig.Default();
    }

    public void RegisterShip(MoveKey key, string shipClass, ShipPosition at)
    {
        if (_units.ContainsKey(key))
        {
            throw new InvalidOperationException($"Корабль {key} уже зарегистрирован на карте");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(shipClass);
        _units[key] = new Unit
        {
            ShipClass = shipClass,
            Position = at,
            Fuel = _config.FuelCapacity, // спавн с полным баком (дефолт)
            ClassMultiplier = _config.SpeedMultiplierOf(shipClass),
        };
    }

    public void SetPath(MoveKey key, IReadOnlyList<Waypoint> path)
    {
        var unit = Require(key);
        ArgumentNullException.ThrowIfNull(path);
        unit.Path = new Queue<Waypoint>(path);
    }

    public void Tick(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(span), span, "Тик не может быть отрицательным");
        }

        if (span == TimeSpan.Zero)
        {
            return;
        }

        var seconds = span.TotalSeconds;
        foreach (var unit in _units.Values)
        {
            MoveUnit(unit, seconds);
        }
    }

    public ShipPosition PositionOf(MoveKey key) => Require(key).Position;

    public double FuelOf(MoveKey key) => Require(key).Fuel;

    public bool IsMoving(MoveKey key) => Require(key).Path.Count > 0;

    private void MoveUnit(Unit unit, double seconds)
    {
        var remaining = seconds;
        double boostedTime = 0; // время на форсаже (жжёт топливо, без регена)
        double plainTime = 0;   // время в обычном ходу (регенерация)
        var burnedOut = false;  // бак опустел на текущем участке — форсаж до его конца выключен

        while (remaining > 0 && unit.Path.Count > 0)
        {
            var waypoint = unit.Path.Peek();
            var boost = waypoint.Afterburner && !burnedOut && unit.Fuel > 0;
            var speed = _config.BaseSpeedUnitsPerSecond
                        * unit.ClassMultiplier
                        * (boost ? _config.AfterburnerSpeedMultiplier : 1.0);
            var distance = unit.Position.DistanceTo(waypoint.Point);
            var travel = Math.Min(speed * remaining, distance);
            var spent = speed > 0 ? travel / speed : 0;

            if (travel > 0)
            {
                unit.Position = unit.Position.StepToward(waypoint.Point, travel);
                remaining -= spent;
                if (boost)
                {
                    boostedTime += spent;
                    unit.Fuel = Math.Max(0, unit.Fuel - _config.AfterburnerFuelPerSecond * spent);
                    if (unit.Fuel <= 0)
                    {
                        burnedOut = true; // топливо кончилось — до конца участка скорость к базовой
                    }
                }
                else
                {
                    plainTime += spent;
                }
            }

            if (unit.Position == waypoint.Point)
            {
                unit.Path.Dequeue(); // доехали до точки — снимаем её (дальше стоит)
            }
        }

        if (remaining > 0 && unit.Path.Count == 0)
        {
            plainTime += remaining; // стоит после участка (или с пустым путём): обычный ход — регенерация
        }

        if (plainTime > 0)
        {
            unit.Fuel = Math.Min(_config.FuelCapacity, unit.Fuel + _config.FuelRegenPerSecond * plainTime);
        }

        // boostedTime/plainTime намеренно не сохраняются: состояние корабля — позиция и бак
    }

    private Unit Require(MoveKey key) =>
        _units.TryGetValue(key, out var unit)
            ? unit
            : throw new InvalidOperationException($"Корабль {key} не зарегистрирован на карте");
}
