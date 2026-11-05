namespace Rts.Domain.Tests;

// ---- Спец-роли классов (support/supply/scout) как необязательная политика полного хода ----
// Дефолт сессий — политика null: спец-роли выключены, старое поведение цело (см. «def null»-сценарии).

public class ClassRolesTests
{
    private static readonly PlayerId P1 = new("p1");
    private static readonly PlayerId P2 = new("p2");
    private static readonly PlayerId P3 = new("p3");
    private static readonly PlayerId P4 = new("p4");
    private static readonly Team TeamA = new(0);
    private static readonly Team TeamB = new(1);

    private static ShipUnit Ship(string id, string @class)
    {
        var profile = TestBalance.Balance.ShipClasses.TryGetValue(@class, out var known)
            ? known
            : TestBalance.Balance.UnknownClassProfile;
        return new ShipUnit(new ShipId(id), @class, profile.Hull, profile.Shield);
    }

    private static ClassRolesUnit Unit(
        string player, byte team, string ship, string klass, ShipPosition at,
        bool alive = true, int hull = 50, int maxHull = 50, double fuel = 100, double fuelCapacity = 100) =>
        new(new PlayerId(player), new Team(team), new ShipId(ship), klass, at, alive, hull, maxHull, fuel, fuelCapacity);

    private static ClassRolesTick Tick1s(params ClassRolesUnit[] units) =>
        new(TimeSpan.FromSeconds(1), units);

    // ---- 1. Ауры support/supply (сервисный уровень) ----

    [Fact]
    public void Саппорт_лечит_только_живых_союзников_в_радиусе()
    {
        var support = Unit("sup", 0, "s1", "support", new ShipPosition(0, 0), hull: 40, maxHull: 70);
        var allyNear = Unit("ally", 0, "s2", "light", new ShipPosition(250, 0), hull: 40);
        var allyOutOfRadius = Unit("far", 0, "s3", "light", new ShipPosition(0, 500), hull: 40); // дальше 300
        var enemyNear = Unit("enemy", 1, "s4", "light", new ShipPosition(10, 0), hull: 40);      // враг рядом
        var deadAlly = Unit("dead", 0, "s5", "light", new ShipPosition(5, 0), alive: false, hull: 0);

        var result = new ClassRolesService().Tick(Tick1s(support, allyNear, allyOutOfRadius, enemyNear, deadAlly));

        // support себя (союзник) лечит тоже; вне радиуса, врага и мёртвых не достаётся
        Assert.Equal(2, result.Applied.Count);
        Assert.All(result.Applied, d =>
        {
            Assert.Equal(5, d.HullHealed);   // +5 корпуса/сек
            Assert.Equal(0, d.FuelAdded);    // лечения без снабженца нет
        });
        Assert.Contains(result.Applied, d => d.Ship == allyNear.Ship);
        Assert.Contains(result.Applied, d => d.Ship == support.Ship);
        Assert.DoesNotContain(result.Applied, d => d.Ship == enemyNear.Ship);      // врагам не лечат
        Assert.DoesNotContain(result.Applied, d => d.Ship == allyOutOfRadius.Ship);
        Assert.DoesNotContain(result.Applied, d => d.Ship == deadAlly.Ship);       // мёртвым не лечат
    }

    [Fact]
    public void Лечение_не_превышает_max_hull()
    {
        var almostFull = Unit("a", 0, "s1", "support", new ShipPosition(0, 0), hull: 47, maxHull: 50);
        var full = Unit("b", 0, "s2", "light", new ShipPosition(100, 0), hull: 50, maxHull: 50);

        var result = new ClassRolesService().Tick(Tick1s(almostFull, full));

        // 47 + 5 → 52, но долечили только 3 (до MaxHull); у полного корпуса эффекта нет вовсе
        var healed = Assert.Single(result.Applied);
        Assert.Equal(almostFull.Ship, healed.Ship);
        Assert.Equal(3, healed.HullHealed);
        Assert.Equal(0, healed.FuelAdded);
    }

    [Fact]
    public void Мёртвый_носитель_не_лечит_и_не_заправляет_мёртвых()
    {
        var deadSupport = Unit("sup1", 0, "s1", "support", new ShipPosition(0, 0), alive: false);
        var woundedAlly = Unit("ally", 0, "s2", "light", new ShipPosition(10, 0), hull: 40); // мёртвый support рядом
        var aliveSupply = Unit("sup2", 0, "s3", "supply", new ShipPosition(20, 0), fuel: 60);
        var deadTarget = Unit("dead", 0, "s4", "light", new ShipPosition(21, 0), alive: false, fuel: 50);

        var result = new ClassRolesService().Tick(Tick1s(deadSupport, woundedAlly, aliveSupply, deadTarget));

        // живой supply работает только по живым (себя заправляет), мёртвой цели не достаётся
        var delta = Assert.Single(result.Applied);
        Assert.Equal(aliveSupply.Ship, delta.Ship);
        Assert.Equal(5, delta.FuelAdded);
        Assert.Equal(0, delta.HullHealed);
        Assert.DoesNotContain(result.Applied, d => d.Ship == deadSupport.Ship);
        Assert.DoesNotContain(result.Applied, d => d.Ship == deadTarget.Ship);
    }

    [Fact]
    public void Снабженец_дозаправляет_только_живых_союзников_в_радиусе()
    {
        var supply = Unit("sup", 0, "s1", "supply", new ShipPosition(0, 0), fuel: 40);
        var allyNear = Unit("ally", 0, "s2", "light", new ShipPosition(250, 0), fuel: 40);
        var allyOutOfRadius = Unit("far", 0, "s3", "light", new ShipPosition(0, 500), fuel: 40); // дальше 300
        var enemyNear = Unit("enemy", 1, "s4", "light", new ShipPosition(5, 0), fuel: 40);       // враг рядом

        var result = new ClassRolesService().Tick(Tick1s(supply, allyNear, allyOutOfRadius, enemyNear));

        Assert.Equal(2, result.Applied.Count); // снабженец себя (союзник) и союзника рядом
        Assert.All(result.Applied, d =>
        {
            Assert.Equal(5, d.FuelAdded); // +5 топлива/сек сверх регена
            Assert.Equal(0, d.HullHealed);
        });
        Assert.Contains(result.Applied, d => d.Ship == allyNear.Ship);
        Assert.Contains(result.Applied, d => d.Ship == supply.Ship);
        Assert.DoesNotContain(result.Applied, d => d.Ship == enemyNear.Ship);
        Assert.DoesNotContain(result.Applied, d => d.Ship == allyOutOfRadius.Ship);
    }

    [Fact]
    public void Дозаправка_не_превышает_бак()
    {
        var supply = Unit("sup", 0, "s1", "supply", new ShipPosition(0, 0), fuel: 40);
        var almostFull = Unit("a", 0, "s2", "light", new ShipPosition(100, 0), fuel: 99);
        var full = Unit("b", 0, "s3", "light", new ShipPosition(100, 0), fuel: 100);

        var result = new ClassRolesService().Tick(Tick1s(supply, almostFull, full));

        // 99 + 5 → 104, но бак только до 100: долили 1; у полного бака эффекта нет
        var refueled = result.Applied.First(d => d.Ship == almostFull.Ship);
        Assert.Equal(1, refueled.FuelAdded);
        Assert.DoesNotContain(result.Applied, d => d.Ship == full.Ship);
    }

    // ---- 2. Уклонение scout (модификатор урона выстрела в такте боя) ----

    [Fact]
    public void Уклонение_scout_уменьшает_получаемый_урон()
    {
        var combat = new CombatService(TestBalance.Balance);
        combat.SetShotModifier(new ClassRolesService()); // уклонение — через модификатор урона выстрела
        var a = new Fleet(P1, TeamA, [Ship("as", "scout")]);                       // 30/10
        var b = new Fleet(P3, TeamB, [Ship("b1", "light"), Ship("b2", "light")]);  // 50/20
        var battle = combat.Start(new MatchId("m"), a, b);

        var tick = combat.Tick(battle);

        // A (scout) стреляет в ПЕРВЫЙ живой light B: 10 урона → щит 20−6=14, корпус 50−4=46
        // (light не уклоняется: уклонение только по классу цели «scout»);
        // B (два живых light) стреляют в scout A: 2×10 ×0.7 = 14 → щит 10−6=4, корпус 30−8=22
        Assert.Equal(14, tick.Battle.TeamB.Ships[0].Shield);
        Assert.Equal(46, tick.Battle.TeamB.Ships[0].Hull);
        Assert.Equal(4, tick.Battle.TeamA.Ships[0].Shield);
        Assert.Equal(22, tick.Battle.TeamA.Ships[0].Hull);

        // без политики было бы: B залпом 20 → гасится 6 → корпус 16 (уклонение сняло 6 корпуса урона)
    }

    // ---- 3. Сессия: ауры применяются после движения; дефолт null ничего не меняет ----

    private static (RoomService Rooms, RoomId Room) BuildRoom()
    {
        var rooms = new RoomService();
        var created = rooms.CreateRoom(P1);
        rooms.Join(created.Room.Id, P2);
        rooms.Join(created.Room.Id, P3);
        rooms.Join(created.Room.Id, P4);
        return (rooms, created.Room.Id);
    }

    private static GameSessionService NewSession(ClassRolesService? classRoles)
    {
        var (rooms, room) = BuildRoom();
        return GameSessionService.StartFromRoom(rooms, room, HostRuleSet.Default(), classRoles: classRoles);
    }

    private static int HullOf(GameSessionService session, PlayerId player) =>
        session.GetState().Ships.First(s => s.Player == player).Hull;

    private static double FuelOf(GameSessionService session, PlayerId player, string shipId) =>
        session.GetState().Ships.First(s => s.Player == player && s.Ship == new ShipId(shipId)).Fuel;

    /// <summary>Саппорт P1 и light P3 едут навстречу, сходятся в зоне боя, стреляют 3 тика; возврат корпуса P1.</summary>
    private static int RunSupportFight(GameSessionService session)
    {
        session.SetFleet(P1, [Ship("p1-sup", "support")]);
        session.SetFleet(P2, [Ship("p2-s", "scout")]);
        session.SetFleet(P3, [Ship("p3-l", "light")]);
        session.SetFleet(P4, [Ship("p4-s", "scout")]);
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.ConfirmFleet(player);
        }

        session.SetPath(P1, new ShipId("p1-sup"), [new Waypoint(new ShipPosition(1500, 1000))]);
        session.SetPath(P3, new ShipId("p3-l"), [new Waypoint(new ShipPosition(500, 1000))]);
        TickSeconds(session, 8);
        return HullOf(session, P1);
    }

    [Fact]
    public void Сессия_саппорт_лечит_себя_но_не_выше_max_hull()
    {
        var healed = RunSupportFight(NewSession(new ClassRolesService()));
        var plain = RunSupportFight(NewSession(null)); // def null: спец-роли выключены — старое поведение

        // без ауры: 3 боевых тика × (10 урона − 6 щита) = 70 − 12 = 58;
        // с аурой: +5 корпуса каждый тик после движения, капа по MaxHull → 70
        Assert.Equal(58, plain);
        Assert.Equal(70, healed);
    }

    /// <summary>Supply P1 («supply») и light P2 спавнятся рядом (60 юнитов) — аура работает сразу.</summary>
    private static (double AfterFirstTick, double Later) RunSupplyRefuel(GameSessionService session)
    {
        session.SetFleet(P1, [Ship("p1-sup", "supply")]);
        session.SetFleet(P2, [Ship("p2-l", "light")]);
        session.SetFleet(P3, [Ship("p3-s", "scout")]);
        session.SetFleet(P4, [Ship("p4-s", "scout")]);
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.ConfirmFleet(player);
        }

        session.SetPath(P2, new ShipId("p2-l"), [new Waypoint(new ShipPosition(5000, 1000), Afterburner: true)]);
        session.SetPath(P1, new ShipId("p1-sup"), [new Waypoint(new ShipPosition(990, 1000))]);
        TickSeconds(session, 1); // тик 1 после движения: форсаж −10, supply в радиусе спавна: +5 сверх регена
        var afterFirstTick = FuelOf(session, P2, "p2-l"); // ON: 100−10+5 = 95; OFF (дефолт): 90
        TickSeconds(session, 1); // тик 2: форсаж −10 ( supply вне радиуса уже) → ON 85, OFF 80
        session.SetPath(P2, new ShipId("p2-l"), []); // стоит: реген 2/с
        TickSeconds(session, 7); // t3..t9: OFF: 82..94; ON: аура с тика 4 → с тика 5 бак полон (100)
        return (afterFirstTick, FuelOf(session, P2, "p2-l"));
    }

    [Fact]
    public void Сессия_supply_дозаправляет_свер_регена_и_не_выше_бака()
    {
        var refueled = RunSupplyRefuel(NewSession(new ClassRolesService()));
        var plain = RunSupplyRefuel(NewSession(null)); // def null: старое поведение (только реген)

        // тик 1 (после движения): у обоих форсаж −10; supply добавляет ровно +5 сверху → 95 против 90;
        // к тику 9 с аурой бак полон ровно 100 (перелива нет); на одном регене — только 94
        Assert.Equal(90, plain.AfterFirstTick);
        Assert.Equal(95, refueled.AfterFirstTick); // +5 сверх регена
        Assert.Equal(94, plain.Later);
        Assert.Equal(100, refueled.Later);         // не превышает бак (при переливе было бы 104)
    }

    private static void TickSeconds(GameSessionService session, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            session.Tick(TimeSpan.FromSeconds(1));
        }
    }
}
