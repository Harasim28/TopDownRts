namespace Rts.Domain.Tests;

// ---- Concede (капитан сдается) и rated-ничья (Эло-политика) ----
// Дефолт сессий — плоская схема: ничья по-прежнему рейтингом не пишется.

public class ConcedeTests
{
    private static readonly PlayerId P1 = new("p1");
    private static readonly PlayerId P2 = new("p2");
    private static readonly PlayerId P3 = new("p3");
    private static readonly PlayerId P4 = new("p4");
    private static readonly Team TeamB = new(1);

    private static ShipUnit Ship(string id, string @class)
    {
        var profile = TestBalance.Balance.ShipClasses.TryGetValue(@class, out var known)
            ? known
            : TestBalance.Balance.UnknownClassProfile;
        return new ShipUnit(new ShipId(id), @class, profile.Hull, profile.Shield);
    }

    private static (RoomService Rooms, RoomId Room) BuildRoom()
    {
        var rooms = new RoomService();
        var created = rooms.CreateRoom(P1);
        rooms.Join(created.Room.Id, P2);
        rooms.Join(created.Room.Id, P3);
        rooms.Join(created.Room.Id, P4);
        return (rooms, created.Room.Id);
    }

    private static GameSessionService NewSession(EloPolicy? ratingPolicy = null)
    {
        var (rooms, room) = BuildRoom();
        return GameSessionService.StartFromRoom(rooms, room, HostRuleSet.Default(), ratingPolicy: ratingPolicy);
    }

    /// <summary>Зеркальные флоты крест-накрест: тяжёлые побеждают scout-ов → лэйны 1:1 → ничья. Все едут в центр.</summary>
    private static void SetupMirrorFleets(GameSessionService session)
    {
        session.SetFleet(P1, [Ship("p1-s", "scout")]);
        session.SetFleet(P2, [Ship("p2-h", "heavy")]);
        session.SetFleet(P3, [Ship("p3-h", "heavy")]);
        session.SetFleet(P4, [Ship("p4-s", "scout")]);
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.ConfirmFleet(player);
        }

        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetPath(player, ShipOf(player), [new Waypoint(new ShipPosition(1000, 1000))]);
        }
    }

    private static ShipId ShipOf(PlayerId player) => player switch
    {
        _ when player == P1 => new ShipId("p1-s"),
        _ when player == P2 => new ShipId("p2-h"),
        _ when player == P3 => new ShipId("p3-h"),
        _ => new ShipId("p4-s"),
    };

    private static void TickUntil(GameSessionService session, Func<bool> done)
    {
        for (var i = 0; i < 600 && !done(); i++)
        {
            session.Tick(TimeSpan.FromSeconds(1));
        }

        Assert.True(done(), "сессия не дошла до ожидаемого состояния за 600 тиков");
    }

    // ---- 1. Concede ----

    [Fact]
    public void Капитан_сдается_его_сторона_проигрывает_и_рейтинг_как_при_поражении()
    {
        var session = NewSession();
        SetupMirrorFleets(session);

        session.Concede(P1); // P1 — капитан A (первый из пары A)

        // сторона A немедленно проигрывает матч
        Assert.Equal(SessionOutcome.TeamBWon, session.Outcome);
        Assert.True(session.IsOver);
        Assert.Equal(TeamB, session.Winner);

        // рейтинг — как при поражении A: B +15 (Wins 1), A −10 (Losses 1)
        var winner = session.Ratings.GetOrCreate(P3);
        Assert.Equal(15, winner.Points);
        Assert.Equal(1, winner.Wins);
        Assert.Equal(-10, session.Ratings.GetOrCreate(P1).Points);
        Assert.Equal(15, session.Ratings.GetOrCreate(P4).Points);
        Assert.Equal(1, session.Ratings.GetOrCreate(P2).Losses);

        // капитан B сдается — то же самое зеркально
        var mirror = NewSession();
        SetupMirrorFleets(mirror);
        mirror.Concede(P3);
        Assert.Equal(SessionOutcome.TeamAWon, mirror.Outcome);
        Assert.Equal(15, mirror.Ratings.GetOrCreate(P1).Points);
        Assert.Equal(-10, mirror.Ratings.GetOrCreate(P3).Points);
    }

    [Fact]
    public void Не_капитану_сдаться_отказ_сессия_продолжается()
    {
        var session = NewSession();
        SetupMirrorFleets(session);

        // P2 и P4 — капитанами не являются (первый из пары)
        Assert.Throws<InvalidOperationException>(() => session.Concede(P2));
        Assert.Throws<InvalidOperationException>(() => session.Concede(P4));

        Assert.False(session.IsOver);
        Assert.Equal(SessionOutcome.None, session.Outcome); // сессия продолжается
    }

    [Fact]
    public void Повторная_сдача_после_исхода_ничего_не_делает()
    {
        var session = NewSession();
        SetupMirrorFleets(session);
        session.Concede(P1);

        var ratingsBefore = (Points: session.Ratings.GetOrCreate(P3).Points, Wins: session.Ratings.GetOrCreate(P3).Wins);

        session.Concede(P3); // после исхода — ничего (ни исключения, ни перезаписи рейтинга)
        session.Concede(P1);

        Assert.Equal(SessionOutcome.TeamBWon, session.Outcome);
        Assert.Equal(ratingsBefore, (session.Ratings.GetOrCreate(P3).Points, session.Ratings.GetOrCreate(P3).Wins));
        Assert.Equal(1, session.Ratings.GetOrCreate(P2).Losses); // проигрыш записан ОДИН раз
    }

    [Fact]
    public void Сдача_с_эло_политикой_пишется_как_поражение_по_политике()
    {
        var session = NewSession(new EloPolicy());
        SetupMirrorFleets(session);

        session.Concede(P1); // A проигрывает: K новичка 40, рейтинги равны → E = 0.5 → A −20, B +20

        Assert.Equal(-20, session.Ratings.GetOrCreate(P1).Points);
        Assert.Equal(-20, session.Ratings.GetOrCreate(P2).Points);
        Assert.Equal(20, session.Ratings.GetOrCreate(P3).Points);
        Assert.Equal(20, session.Ratings.GetOrCreate(P4).Points);
        Assert.Equal(1, session.Ratings.GetOrCreate(P1).Losses); // счётчик поражения по политике
        Assert.Equal(1, session.Ratings.GetOrCreate(P3).Wins);
    }

    // ---- 2. Ничья: с Эло-политикой пишется (S = 0.5), плоская — как раньше, не пишется ----

    [Fact]
    public void Ничья_с_эло_политикой_пишет_обе_пары_с_S_05()
    {
        var session = NewSession(new EloPolicy());
        // посев ladder'а: применяем «победу A» напрямую через хранилище политики,
        // чтобы обновления ничьей было видно (при равных рейтингах дельты нулевые)
        var seeded = (IPolicyRatingRepository)session.Ratings;
        seeded.Apply(new RatingGameResult([P1, P2], [P3, P4], RatingGameWinner.TeamA)); // A: +20, B: −20

        SetupMirrorFleets(session); // пересоздадим: рейтинг уже общий с сессией
        TickUntil(session, () => session.IsOver);

        Assert.Equal(SessionOutcome.Draw, session.Outcome);
        // ничья: S = 0.5 обеим сторонам (EloConfig.DrawScore): A (20 очков, E ≈ 0.557) −2, B +2
        Assert.Equal(18, session.Ratings.GetOrCreate(P1).Points);
        Assert.Equal(18, session.Ratings.GetOrCreate(P2).Points);
        Assert.Equal(-18, session.Ratings.GetOrCreate(P3).Points);
        Assert.Equal(-18, session.Ratings.GetOrCreate(P4).Points);
        // счётчики ничьёй не двигаются (победа/поражение засчитаны при посеве)
        Assert.Equal(1, session.Ratings.GetOrCreate(P1).Wins);
        Assert.Equal(0, session.Ratings.GetOrCreate(P1).Losses);
    }

    [Fact]
    public void Плоская_ничья_как_раньше_рейтингом_не_пишется()
    {
        var session = NewSession();
        SetupMirrorFleets(session);
        TickUntil(session, () => session.IsOver);

        Assert.Equal(SessionOutcome.Draw, session.Outcome);
        var rating = session.Ratings.GetOrCreate(P1);
        Assert.Equal(0, rating.Points);
        Assert.Equal(0, rating.Wins);
        Assert.Equal(0, rating.Losses);
    }
}
