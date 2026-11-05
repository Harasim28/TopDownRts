namespace Rts.Domain.Tests;

/// <summary>
/// Интеграционные сценарии «полного игрового хода» (кадры 1 + 3a/3b + 4a/4b + 5):
/// карта и движение — прямо через IBaseMap/IFleetMovementService (числа берём из конфига),
/// полный ход 2v2 — через IGameSession: комната → лобби → закуп → MatchStarted →
/// движение → базы → бой лэйнов → исход → рейтинг.
/// </summary>
public class GameSessionTests
{
    private static readonly PlayerId P1 = new("p1");
    private static readonly PlayerId P2 = new("p2");
    private static readonly PlayerId P3 = new("p3");
    private static readonly PlayerId P4 = new("p4");
    private static readonly Team TeamA = new(0);
    private static readonly Team TeamB = new(1);

    private static MovementConfig Cfg => MovementConfig.Default();

    // ---- Карта и движение (кадр 4a): через карты/движение домена, числа — из MovementConfig ----

    [Fact]
    public void Дефолтная_карта_определяет_зоны_баз_по_точкам_и_радиусу_из_конфига()
    {
        var map = new BattleMap(Cfg);
        var points = Cfg.BasePoints;

        Assert.Equal(points[BaseId.HomeA], map.CenterOf(BaseId.HomeA));
        Assert.Equal(points[BaseId.Central], map.CenterOf(BaseId.Central));
        Assert.Equal(points[BaseId.HomeB], map.CenterOf(BaseId.HomeB));

        Assert.Equal(points[BaseId.HomeA], map.CenterOf(BaseId.HomeA)); // дом A на своей точке
        Assert.Equal(BaseId.HomeA, map.TryZoneOf(points[BaseId.HomeA]));
        Assert.Equal(BaseId.Central, map.TryZoneOf(points[BaseId.Central]));
        Assert.Equal(BaseId.HomeB, map.TryZoneOf(points[BaseId.HomeB]));

        // граница зоны: на радиусе — в зоне, за радиусом — вне (радиус из конфига)
        var a = points[BaseId.HomeA];
        Assert.Equal(BaseId.HomeA, map.TryZoneOf(a + new ShipPosition(Cfg.BaseZoneRadius, 0)));
        Assert.Null(map.TryZoneOf(a + new ShipPosition(Cfg.BaseZoneRadius + 1, 0)));
        Assert.Null(map.TryZoneOf(new ShipPosition(200, 1000))); // пустой угол карты
    }

    [Fact]
    public void Скаут_доезжает_до_точки_за_расчётное_время_и_дальше_стоит()
    {
        var movement = new FleetMovementService(Cfg);
        var start = Cfg.BasePoints[BaseId.HomeA];
        var target = Cfg.BasePoints[BaseId.Central]; // 600 юнитов по оси X на дефолт-карте
        var key = new MoveKey(P1, new ShipId("s1"));
        movement.RegisterShip(key, "scout", start);
        movement.SetPath(key, [new Waypoint(target)]);

        var speed = Cfg.BaseSpeedUnitsPerSecond * Cfg.ClassSpeedMultipliers["scout"]; // 180 ед/с
        var seconds = start.DistanceTo(target) / speed;                               // ≈ 3,33 с

        // чуть раньше срока — ещё в пути, позиция движется строго по скорости
        movement.Tick(TimeSpan.FromSeconds(seconds - 0.01));
        Assert.True(movement.IsMoving(key));
        Assert.True((movement.PositionOf(key).X - start.X) < target.X - start.X);

        movement.Tick(TimeSpan.FromSeconds(0.1)); // докатывание до точки
        Assert.Equal(target, movement.PositionOf(key));
        Assert.False(movement.IsMoving(key));

        // «докинул до точки — стоит»: тики стоят на месте
        movement.Tick(TimeSpan.FromSeconds(1));
        movement.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(target, movement.PositionOf(key));
    }

    [Fact]
    public void Скорость_пути_складывается_из_класса_а_неизвестный_класс_идёт_как_медиум()
    {
        var movement = new FleetMovementService(Cfg);
        var scout = new MoveKey(P1, new ShipId("sc"));
        var heavy = new MoveKey(P2, new ShipId("hv"));
        var unknown = new MoveKey(P3, new ShipId("un"));
        movement.RegisterShip(scout, "scout", new ShipPosition(0, 0));
        movement.RegisterShip(heavy, "heavy", new ShipPosition(0, 0));
        movement.RegisterShip(unknown, "linac", new ShipPosition(0, 0)); // класс вне словаря
        movement.SetPath(scout, [new Waypoint(new ShipPosition(5000, 0))]);
        movement.SetPath(heavy, [new Waypoint(new ShipPosition(5000, 0))]);
        movement.SetPath(unknown, [new Waypoint(new ShipPosition(5000, 0))]);

        movement.Tick(TimeSpan.FromSeconds(1));

        Assert.Equal(Cfg.BaseSpeedUnitsPerSecond * Cfg.ClassSpeedMultipliers["scout"], movement.PositionOf(scout).X);
        Assert.Equal(Cfg.BaseSpeedUnitsPerSecond * Cfg.ClassSpeedMultipliers["heavy"], movement.PositionOf(heavy).X);
        Assert.Equal(Cfg.BaseSpeedUnitsPerSecond * Cfg.UnknownClassSpeedMultiplier, movement.PositionOf(unknown).X);
    }

    [Fact]
    public void Форсаж_удваивает_скорость_и_списывает_десять_топлива_в_секунду()
    {
        var movement = new FleetMovementService(Cfg);
        var key = new MoveKey(P1, new ShipId("s1"));
        movement.RegisterShip(key, "scout", new ShipPosition(0, 0));
        movement.SetPath(key, [new Waypoint(new ShipPosition(5000, 0), Afterburner: true)]);

        var boostSpeed = Cfg.BaseSpeedUnitsPerSecond * Cfg.ClassSpeedMultipliers["scout"]
                         * Cfg.AfterburnerSpeedMultiplier; // 360

        movement.Tick(TimeSpan.FromSeconds(1));

        Assert.Equal(boostSpeed, movement.PositionOf(key).X);
        Assert.Equal(Cfg.FuelCapacity - Cfg.AfterburnerFuelPerSecond, movement.FuelOf(key)); // 100 − 10
    }

    [Fact]
    public void Регенерация_идёт_в_обычном_ходу_а_в_форсаже_нет_и_не_переливает_бак()
    {
        var movement = new FleetMovementService(Cfg);
        var key = new MoveKey(P1, new ShipId("s1"));
        movement.RegisterShip(key, "scout", new ShipPosition(0, 0));
        movement.SetPath(key, [new Waypoint(new ShipPosition(5000, 0), Afterburner: true)]);

        movement.Tick(TimeSpan.FromSeconds(1)); // форсаж: топливо сожжено, без регена
        Assert.Equal(Cfg.FuelCapacity - Cfg.AfterburnerFuelPerSecond, movement.FuelOf(key));

        movement.SetPath(key, []); // стоим (обычный ход)
        movement.Tick(TimeSpan.FromSeconds(1));
        movement.Tick(TimeSpan.FromSeconds(1));
        movement.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(Cfg.FuelCapacity - Cfg.AfterburnerFuelPerSecond + 3 * Cfg.FuelRegenPerSecond, movement.FuelOf(key));

        // бак не переливается выше ёмкости: свежий корабль с маленьким баком стоит и регенится
        var tiny = new MovementConfig { FuelCapacity = 10, FuelRegenPerSecond = 3 };
        var service = new FleetMovementService(tiny);
        var key2 = new MoveKey(P2, new ShipId("s2"));
        service.RegisterShip(key2, "scout", new ShipPosition(0, 0));
        service.Tick(TimeSpan.FromSeconds(1));
        service.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(tiny.FuelCapacity, service.FuelOf(key2));
    }

    [Fact]
    public void Топливо_кончилось_скорость_падает_к_базовой_до_конца_участка()
    {
        var movement = new FleetMovementService(Cfg);
        var key = new MoveKey(P1, new ShipId("s1"));
        // тяжёлый: форсаж 140 ед/с, бак 100 при расходе 10/с → 10 с форсажа = 1400 юнитов
        movement.RegisterShip(key, "heavy", new ShipPosition(0, 0));
        movement.SetPath(key, [new Waypoint(new ShipPosition(1620, 0), Afterburner: true)]);

        var boostSpeed = Cfg.BaseSpeedUnitsPerSecond * Cfg.ClassSpeedMultipliers["heavy"]
                         * Cfg.AfterburnerSpeedMultiplier;               // 140
        var dryTicks = Cfg.FuelCapacity / Cfg.AfterburnerFuelPerSecond;  // 10 тиков по 1 с
        for (var i = 0; i < dryTicks; i++) movement.Tick(TimeSpan.FromSeconds(1));

        Assert.Equal(boostSpeed * dryTicks, movement.PositionOf(key).X); // 1400 — всё ещё на форсаже
        Assert.Equal(0, movement.FuelOf(key));
        Assert.True(movement.IsMoving(key));

        // дальше участка: обычная скорость (база × класс), топливо начало регенерироваться;
        // на этом же участке форсаж не включается снова
        movement.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(boostSpeed * dryTicks + Cfg.BaseSpeedUnitsPerSecond * Cfg.ClassSpeedMultipliers["heavy"],
            movement.PositionOf(key).X);
        Assert.Equal(Cfg.FuelRegenPerSecond, movement.FuelOf(key));

        // доезжает до точки в итоге и стоит
        while (movement.IsMoving(key)) movement.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(new ShipPosition(1620, 0), movement.PositionOf(key));
        Assert.False(movement.IsMoving(key));
    }
}
