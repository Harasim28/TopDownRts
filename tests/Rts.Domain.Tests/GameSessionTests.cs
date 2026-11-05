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

    // ---- Полный игровой ход 2v2 (кадры 1 + 3 + 4 + 5): через IGameSession ----

    private static GameSessionService NewSession(HostRuleSet? rules = null, MovementConfig? movement = null)
    {
        var rooms = new RoomService();
        var created = rooms.CreateRoom(P1);
        rooms.Join(created.Room.Id, P2);
        rooms.Join(created.Room.Id, P3);
        rooms.Join(created.Room.Id, P4);
        return GameSessionService.StartFromRoom(rooms, created.Room.Id, rules ?? HostRuleSet.Default(), movement: movement);
    }

    private static ShipUnit[] Ships(string prefix, string @class, int count) =>
        Enumerable.Range(0, count).Select(i => Ship($"{prefix}{i}", @class)).ToArray();

    private static ShipUnit Ship(string id, string @class)
    {
        var profile = TestBalance.Balance.ShipClasses.TryGetValue(@class, out var known)
            ? known
            : TestBalance.Balance.UnknownClassProfile;
        return new ShipUnit(new ShipId(id), @class, profile.Hull, profile.Shield);
    }

    private static void TickSeconds(GameSessionService session, int ticks, double seconds = 1)
    {
        for (var i = 0; i < ticks; i++) session.Tick(TimeSpan.FromSeconds(seconds));
    }

    private static void TickUntil(GameSessionService session, Func<bool> done, int maxTicks = 40)
    {
        for (var i = 0; i < maxTicks && !done(); i++) session.Tick(TimeSpan.FromSeconds(1));
        Assert.True(done(), $"условие не выполнилось за {maxTicks} тиков по 1 с");
    }

    private static SessionShipState ShipState(GameSessionState state, PlayerId player) =>
        state.Ships.First(s => s.Player == player);

    private static SessionBaseState BaseState(GameSessionState state, BaseId baseId) =>
        state.Bases.Single(b => b.Base == baseId);

    private static ShipId S(string id) => new(id);

    [Fact]
    public void Полный_ход_от_комнаты_до_MatchStarted_спавнит_восемь_кораблей_у_домашних_баз()
    {
        var session = NewSession();

        // закуп и подтверждение: пара лэйна стартует на втором подтверждении
        session.SetFleet(P1, Ships("a", "scout", 2));
        Assert.Null(session.ConfirmFleet(P1));
        session.SetFleet(P3, Ships("b", "scout", 2));
        Assert.NotNull(session.ConfirmFleet(P3)); // первый MatchStarted и спавн лэйна 1

        session.SetFleet(P2, Ships("a2", "scout", 2));
        Assert.Null(session.ConfirmFleet(P2));
        session.SetFleet(P4, Ships("b2", "scout", 2));
        Assert.NotNull(session.ConfirmFleet(P4)); // второй MatchStarted и спавн лэйна 2

        var state = session.GetState();
        Assert.Equal(8, state.Ships.Count);
        foreach (var ship in state.Ships)
        {
            var homeSide = ship.Team == TeamA ? Cfg.BasePoints[BaseId.HomeA] : Cfg.BasePoints[BaseId.HomeB];
            Assert.True(ship.Position.DistanceTo(homeSide) <= Cfg.BaseZoneRadius,
                "корабли спавнятся в зоне своей домашней базы");
        }

        // лобби-фаза прошла честно: оба матча собраны и в BothReady
        Assert.Equal(ReadyState.BothReady, session.Lobby.TryGetMatch(P1)!.State);
        Assert.Equal(ReadyState.BothReady, session.Lobby.TryGetMatch(P4)!.State);
    }

    [Fact]
    public void Сессия_делит_четырёх_участников_пополам_по_порядку_и_сводит_лэйны_крест_накрест()
    {
        var session = NewSession();

        Assert.Equal(new[] { P1, P2 }, session.TeamAPlayers);
        Assert.Equal(new[] { P3, P4 }, session.TeamBPlayers);

        var lane1 = session.Lobby.TryGetMatch(P1)!; // спаривание крест-накрест: A(0)↔B(0), A(1)↔B(1)
        var lane2 = session.Lobby.TryGetMatch(P2)!;
        Assert.Equal(P3, lane1.B);
        Assert.Equal(P4, lane2.B);
    }

    [Fact]
    public void До_подтверждения_флотов_сессия_не_спавнит_корабли_и_тики_делают_ничего()
    {
        var session = NewSession();
        session.SetFleet(P1, Ships("a", "scout", 1));
        Assert.Null(session.ConfirmFleet(P1)); // второй игрок лэйна не подтвердил

        TickSeconds(session, 5);
        for (var i = 0; i < 3; i++) session.Tick(TimeSpan.FromSeconds(1));

        var state = session.GetState();
        Assert.Empty(state.Ships);
        Assert.False(state.BattleStarted);
        Assert.Equal(SessionOutcome.None, session.Outcome);
        Assert.False(session.IsOver);
    }

    [Fact]
    public void Скаут_сессии_доезжает_до_центра_за_расчётное_время_и_захватывает_базу()
    {
        var session = NewSession();
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetFleet(player, [Ship($"{player.Value}-s", "scout")]);
        }

        foreach (var player in new[] { P1, P2, P3, P4 }) session.ConfirmFleet(player);
        TickSeconds(session, 2);

        session.SetPath(P1, S("p1-s"), [new Waypoint(Cfg.BasePoints[BaseId.Central])]);
        TickUntil(session, () => ShipState(session.GetState(), P1).Position == Cfg.BasePoints[BaseId.Central]);

        // противник у себя дома (тоже доезжает и стоит) — боя на таком раздорожье нет (WeaponRange)
        var central = BaseState(session.GetState(), BaseId.Central);
        Assert.Null(central.Owner);

        // захват виден в моменте: кто копит и сколько накопил; владение переходит к A
        Team? capturingSeen = TeamB; // заведомо не совпадёт с «копирующим»
        TickUntil(session, () =>
        {
            var baseState = BaseState(session.GetState(), BaseId.Central);
            if (baseState.Owner == TeamA) return true;
            capturingSeen = baseState.Capturing;
            if (baseState.Capturing == TeamA)
            {
                Assert.True(baseState.CaptureProgressSeconds > 0, "прогресс захвата растёт с присутствием");
            }

            return false;
        }, 40);
        Assert.Equal(TeamA, capturingSeen); // захват вёлся командой A
        var captured = BaseState(session.GetState(), BaseId.Central);
        Assert.Null(captured.Capturing); // база наша — никто больше не копит

        // враг никуда не доехал: стоял в своей зоне всё время
        Assert.True(ShipState(session.GetState(), P3).Position.DistanceTo(Cfg.BasePoints[BaseId.HomeB])
                    <= Cfg.BaseZoneRadius);
    }

    [Fact]
    public void Поочерёдный_захват_всех_баз_даёт_победу_по_таймеру_удержания()
    {
        var rules = new HostRuleSet(holdAllBasesMinutes: 2, victoryScoreThreshold: 2000); // дефолты сценария
        var session = NewSession(rules);
        foreach (var player in new[] { P1, P2 })
        {
            session.SetFleet(player, Ships(player.Value, "scout", 2));
        }

        foreach (var player in new[] { P3, P4 })
        {
            session.SetFleet(player, Ships(player.Value, "scout", 1));
        }

        foreach (var player in new[] { P1, P2, P3, P4 }) session.ConfirmFleet(player);
        TickSeconds(session, 2);

        // сторона B уходит из своей зоны и стоит за пределами дальности боя
        foreach (var player in new[] { P3, P4 })
        {
            session.SetPath(player, S($"{player.Value}0"), [new Waypoint(new ShipPosition(1600, 1400))]);
        }

        // сторона A идёт захватывать центр: сначала туда — это первый поочерёдный шаг
        foreach (var player in new[] { P1, P2 })
        {
            foreach (var ship in session.GetState().Ships.Where(s => s.Player == player))
            {
                session.SetPath(player, ship.Ship, [new Waypoint(Cfg.BasePoints[BaseId.Central])]);
            }
        }

        TickUntil(session, () => BaseState(session.GetState(), BaseId.Central).Owner == TeamA);

        // поочерёдный захват: за центром — чужой дом
        foreach (var player in new[] { P1, P2 })
        {
            foreach (var ship in session.GetState().Ships.Where(s => s.Player == player))
            {
                session.SetPath(player, ship.Ship, [new Waypoint(Cfg.BasePoints[BaseId.HomeB])]);
            }
        }

        TickUntil(session, () => BaseState(session.GetState(), BaseId.HomeB).Owner == TeamA, 60);

        TickUntil(session, () => session.IsOver, 220);
        Assert.Equal(SessionOutcome.TeamAWon, session.Outcome);
        Assert.Equal(TeamA, session.Winner);

        var state = session.GetState();
        Assert.True(state.HoldAllSecondsTeamA >= rules.HoldAllBasesMinutes * 60,
            "победа по таймеру владения всеми тремя базами");
        Assert.True(state.ScoreTeamA < rules.VictoryScoreThreshold, "победа не по очкам: порог не набран");

        // сторона B жива — исход решён базами, а не уничтожением
        Assert.True(state.Ships.Where(s => s.Player == P3 || s.Player == P4).All(s => s.Alive));
    }

    [Fact]
    public void Уничтожение_обеих_пар_противника_даёт_победу_A_и_рейтинг_плюс15_минус10()
    {
        var session = NewSession();
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetFleet(player, [Ship($"{player.Value}-s", "scout")]);
            session.ConfirmFleet(player);
        }

        // пути заказываем после спавна: корабли известны сессии после MatchStarted лэйна
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetPath(player, S($"{player.Value}-s"), [new Waypoint(Cfg.BasePoints[BaseId.Central])]);
        }

        TickUntil(session, () => session.IsOver, 30);
        Assert.Equal(SessionOutcome.TeamAWon, session.Outcome);
        Assert.Equal(TeamA, session.Winner);

        var state = session.GetState();
        Assert.True(state.Ships.Where(s => s.Player == P3 || s.Player == P4).All(s => !s.Alive), "флоты B уничтожены");
        Assert.True(state.Ships.Where(s => s.Player == P1 || s.Player == P2).Any(s => s.Alive), "у A остались корабли");

        // рейтинг «как есть»: у каждого игрока ровно одна запись (±15/−10, победа/поражение)
        var balance = TestBalance.Balance;
        foreach (var winner in new[] { P1, P2 })
        {
            var rating = session.Ratings.GetOrCreate(winner);
            Assert.Equal(balance.RatingWinPoints, rating.Points);
            Assert.Equal(1, rating.Wins);
            Assert.Equal(0, rating.Losses);
        }

        foreach (var loser in new[] { P3, P4 })
        {
            var rating = session.Ratings.GetOrCreate(loser);
            Assert.Equal(-balance.RatingLosePoints, rating.Points);
            Assert.Equal(0, rating.Wins);
            Assert.Equal(1, rating.Losses);
        }
    }

    [Fact]
    public void Зеркальный_размен_по_победе_на_лейн_даёт_ничью_и_рейтинг_0_0()
    {
        var session = NewSession();
        // зеркальные силы: по слабому против сильного в каждом лэйне
        session.SetFleet(P1, [Ship("p1-s", "scout")]); // лейн 1: A-скаут против B-тяжёлого
        session.SetFleet(P2, [Ship("p2-h", "heavy")]); // лейн 2: A-тяжёлый против B-скаута
        session.SetFleet(P3, [Ship("p3-h", "heavy")]);
        session.SetFleet(P4, [Ship("p4-s", "scout")]);
        foreach (var player in new[] { P1, P2, P3, P4 }) session.ConfirmFleet(player);
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            var isScout = player == P1 || player == P4;
            var target = isScout ? "s" : "h"; // каждому — его корабль
            session.SetPath(player, S($"{player.Value}-{target}"), [new Waypoint(Cfg.BasePoints[BaseId.Central])]);
        }

        TickUntil(session, () => session.IsOver, 30);

        // каждая сторона потратила по одному лэйну («одинаковое уничтожение»), живые остались у обеих
        Assert.Equal(SessionOutcome.Draw, session.Outcome);
        Assert.Null(session.Winner);
        var state = session.GetState();
        Assert.True(state.Ships.Count(s => s.Alive) == 2, "после размена жив один корабль с каждой стороны");
        Assert.True(state.Ships.Count(s => (s.Player == P1 || s.Player == P4) && !s.Alive) == 2, "погибли оба скаута");

        // ничья: рейтинг не пишется — у всех нули (дефолт «0/0»)
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            var rating = session.Ratings.GetOrCreate(player);
            Assert.True(rating.Points == 0 && rating.Wins == 0 && rating.Losses == 0, $"у {player.Value} 0/0");
        }
    }

    [Fact]
    public void После_исхода_сессия_замерзает_и_рейтинг_не_дописывается()
    {
        var session = NewSession();
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetFleet(player, [Ship($"{player.Value}-s", "scout")]);
            session.ConfirmFleet(player);
        }

        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetPath(player, S($"{player.Value}-s"), [new Waypoint(Cfg.BasePoints[BaseId.Central])]);
        }

        TickUntil(session, () => session.IsOver, 30);
        Assert.Equal(SessionOutcome.TeamAWon, session.Outcome);
        var before = SessionSnapshot(session);

        TickSeconds(session, 10);

        // мутирующие команды после исхода запрещены
        Assert.Throws<InvalidOperationException>(() => session.SetPath(P1, S("p1-s"), []));
        Assert.Throws<InvalidOperationException>(() => session.SetFleet(P2, [Ship("x", "scout")]));
        Assert.Throws<InvalidOperationException>(() => session.ConfirmFleet(P2));

        var after = SessionSnapshot(session);
        Assert.Equal(SessionOutcome.TeamAWon, after.Outcome); // исход не меняется
        Assert.Equal(before.Outcome, after.Outcome);
        Assert.Equal<IEnumerable<(ShipId, ShipPosition, double, bool)>>(before.Ships, after.Ships);
        Assert.Equal<IEnumerable<(BaseId, Team?, Team?, double)>>(before.Bases, after.Bases);
        Assert.Equal(before.ScoreA, after.ScoreA);
        Assert.Equal(before.ScoreB, after.ScoreB);
        Assert.Equal(before.HoldA, after.HoldA);
        Assert.Equal(before.HoldB, after.HoldB);

        // рейтинг записан однократно, «как есть» (±15/−10)
        var balance = TestBalance.Balance;
        Assert.Equal((1, 0), (session.Ratings.GetOrCreate(P1).Wins, session.Ratings.GetOrCreate(P1).Losses));
        Assert.Equal(balance.RatingWinPoints, session.Ratings.GetOrCreate(P1).Points);
        Assert.Equal(1, session.Ratings.GetOrCreate(P3).Losses); // запись не продублирована
        Assert.Equal(-balance.RatingLosePoints, session.Ratings.GetOrCreate(P4).Points);
    }

    private static (List<(ShipId, ShipPosition, double, bool)> Ships, List<(BaseId, Team?, Team?, double)> Bases,
        double ScoreA, double ScoreB, double HoldA, double HoldB, SessionOutcome Outcome) SessionSnapshot(
        GameSessionService session)
    {
        var state = session.GetState();
        return (
            state.Ships.Select(s => (s.Ship, s.Position, s.Fuel, s.Alive)).ToList(),
            state.Bases.Select(b => (b.Base, b.Owner, b.Capturing, b.CaptureProgressSeconds)).ToList(),
            state.ScoreTeamA, state.ScoreTeamB, state.HoldAllSecondsTeamA, state.HoldAllSecondsTeamB, state.Outcome);
    }

    [Fact]
    public void GetState_отражает_позиции_топливо_владение_базами_очки_и_hold()
    {
        var session = NewSession();
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetFleet(player, [Ship($"{player.Value}-s", "scout")]);
        }

        foreach (var player in new[] { P1, P2, P3, P4 }) session.ConfirmFleet(player);
        TickSeconds(session, 2);

        session.SetPath(P1, S("p1-s"), [new Waypoint(Cfg.BasePoints[BaseId.Central])]);
        TickSeconds(session, 5); // скаут в пути к центру (без форсажа)

        var state = session.GetState();
        var scout = ShipState(state, P1);
        Assert.True(scout.Position.X > Cfg.BasePoints[BaseId.HomeA].X, "корабль поехал к центру");
        Assert.True(scout.Fuel > 0, "топливо видно клиенту в [0..ёмкость]");
        Assert.Equal(Cfg.FuelCapacity, scout.Fuel); // без форсажа расхода нет
        Assert.True(scout.Alive);

        var homeA = BaseState(state, BaseId.HomeA);
        Assert.Equal(TeamA, homeA.Owner);
        Assert.Null(homeA.Capturing);
        var homeB = BaseState(state, BaseId.HomeB);
        Assert.Equal(TeamB, homeB.Owner);
        var central = BaseState(state, BaseId.Central);
        Assert.Null(central.Owner);

        Assert.True(state.ScoreTeamA > 0, "очки за свой дом тикают");
        Assert.True(state.ScoreTeamB > 0);
        Assert.Equal(0, state.HoldAllSecondsTeamA); // всех трёх баз нет — таймер не идёт
        Assert.False(session.IsOver);
        Assert.Equal(SessionOutcome.None, state.Outcome);
    }

    [Fact]
    public void Перебор_капы_закупа_отклоняется_до_мутаций()
    {
        var session = NewSession();
        var cap = TestBalance.Balance.PurchaseCapPoints;
        var over = new List<ShipUnit>();
        var cost = 0;
        foreach (var (classs, klass) in TestBalance.Balance.ShipClasses.OrderBy(kv => kv.Value.Cost))
        {
            while (cost + klass.Cost <= cap)
            {
                over.Add(Ship($"{classs}{cost}", classs));
                cost += klass.Cost;
            }
        }

        var cheapest = TestBalance.Balance.ShipClasses.Values.Min(p => p.Cost);
        over.Add(Ship("overflow", "light")); // ровно на один самый дешёвый сверх капы

        Assert.True(cost + cheapest > cap, "состав собран с перебором капы");
        Assert.Throws<InvalidOperationException>(() => session.SetFleet(P1, over));
        Assert.Throws<InvalidOperationException>(() => session.ConfirmFleet(P1)); // состава нет — подтверждать нечего
    }
}

