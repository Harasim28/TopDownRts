namespace Rts.Domain.Tests;

// ---- P11: спад урона от дистанции как необязательная политика полного хода ----
// Дефолт сессий — конфиг null: урон без спада, старое поведение цело.

public class DamageFalloffTests
{
    private static readonly PlayerId PA = new("pa");
    private static readonly PlayerId PB = new("pb");
    private static readonly Team TeamA = new(0);
    private static readonly Team TeamB = new(1);

    private static ShipUnit Ship(string id) => new(new ShipId(id), "light", 50, 20);

    /// <summary>Политика с картой позиций стрелок/цель (контракты P11 проверяются по дистанциям).</summary>
    private static DamageFalloffPolicy Policy(double from, double to) =>
        new(key => key.Owner == PA ? new ShipPosition(from, 0) : new ShipPosition(to, 0), 300);

    private static double Multiplier(double distance) =>
        Policy(0, distance).ShotMultiplierOf(PA, Ship("a"), PB, Ship("b"));

    [Fact]
    public void Вблизи_CloseRange_урон_полный()
    {
        Assert.Equal(1.0, Multiplier(100));
        Assert.Equal(1.0, Multiplier(150)); // граница близкой дистанции включительно
    }

    [Fact]
    public void Середина_спада_линейна()
    {
        // середина между 150 (урон 100%) и 300 (минимум 0.4): 0.4 + 0.6 × (300−225)/(300−150) = 0.7
        Assert.Equal(0.7, Multiplier(225), precision: 9);
    }

    [Fact]
    public void На_WeaponRange_минимум_x04_и_дальше_не_падает()
    {
        Assert.Equal(0.4, Multiplier(300), precision: 9); // граница дальности: минимум
        Assert.Equal(0.4, Multiplier(350), precision: 9); // дальше триггера боя тик и не запускается
    }

    [Fact]
    public void Спад_в_тике_боя_щит_гасит_не_больше_нанесённого()
    {
        var positions = new Dictionary<MoveKey, ShipPosition>
        {
            [new MoveKey(PA, new ShipId("a"))] = new(0, 0),
            [new MoveKey(PB, new ShipId("b"))] = new(300, 0), // на границе дальности: множитель 0.4
        };
        var combat = new CombatService(TestBalance.Balance);
        combat.SetShotModifier(new DamageFalloffPolicy(key => positions[key], 300));
        var battle = combat.Start(new MatchId("m"),
            new Fleet(PA, TeamA, [Ship("a")]),
            new Fleet(PB, TeamB, [Ship("b")]));

        var tick = combat.Tick(battle);

        // урон 10 × 0.4 = 4: щит гасит только 4 (а не регламентные 6), корпус цел;
        // обратный огонь симметричен (дистанция та же)
        Assert.Equal(16, tick.Battle.TeamB.Ships[0].Shield);
        Assert.Equal(50, tick.Battle.TeamB.Ships[0].Hull);
        Assert.Equal(16, tick.Battle.TeamA.Ships[0].Shield);
        Assert.Equal(50, tick.Battle.TeamA.Ships[0].Hull);
        Assert.Equal(0, tick.HullDamageToTeamA); // в корпус ушло 4−4 = 0
        Assert.Equal(0, tick.HullDamageToTeamB);
    }

    // ---- Сессия: спад через тик боёв; def null — прежнее поведение ----

    private static (RoomService Rooms, RoomId Room) BuildRoom()
    {
        var rooms = new RoomService();
        var created = rooms.CreateRoom(new PlayerId("p1"));
        rooms.Join(created.Room.Id, new PlayerId("p2"));
        rooms.Join(created.Room.Id, new PlayerId("p3"));
        rooms.Join(created.Room.Id, new PlayerId("p4"));
        return (rooms, created.Room.Id);
    }

    private static GameSessionService NewSession(DamageFalloffConfig? falloff)
    {
        var (rooms, room) = BuildRoom();
        return GameSessionService.StartFromRoom(rooms, room, HostRuleSet.Default(), damageFalloff: falloff);
    }

    private static int HullOf(GameSessionService session, PlayerId player) =>
        session.GetState().Ships.First(s => s.Player == player).Hull;

    /// <summary>Light P1 едет к light P3, останавливается на дистанции 270 (спад 0.52) и оба стоят под огнём.</summary>
    private static int RunParkedVolley(GameSessionService session)
    {
        session.SetFleet(new PlayerId("p1"), [Ship("a")]);
        session.SetFleet(new PlayerId("p2"), [Ship("a2")]);
        session.SetFleet(new PlayerId("p3"), [Ship("b")]);
        session.SetFleet(new PlayerId("p4"), [Ship("b2")]);
        foreach (var player in new[] { "p1", "p2", "p3", "p4" })
        {
            session.ConfirmFleet(new PlayerId(player));
        }

        // спавн p1: (370,1000); едет к (1300,1000): дистанция до p3 (1570,1000) → 270, спад ×0.52
        session.SetPath(new PlayerId("p1"), new ShipId("a"), [new Waypoint(new ShipPosition(1300, 1000))]);
        TickSeconds(session, 11); // боевые тики с t7 (сошлись на 270) — 5 тиков огня у всех на месте
        return HullOf(session, new PlayerId("p3"));
    }

    [Fact]
    public void Сессия_со_спадом_на_дальней_урон_меньше()
    {
        var faded = RunParkedVolley(NewSession(new DamageFalloffConfig()));
        var plain = RunParkedVolley(NewSession(null)); // def null: прежний урон без спада

        // 5 боевых тиков на дистанции 270: спад ×0.52 → урон 5 в тик (щит гасит 5) против 10 у дефолта;
        // корпус P3: 50 − 5 = 45 со спадом и 50 − (8+10) = 20 без
        Assert.Equal(20, plain);
        Assert.Equal(45, faded);
    }

    private static void TickSeconds(GameSessionService session, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            session.Tick(TimeSpan.FromSeconds(1));
        }
    }
}
