namespace Rts.Domain.Tests;

/// <summary>
/// Сценарии шахматного Эло (FIDE) по IRatingPolicy/EloPolicy: ступени K (новичок 40 → 30 игр → 20 →
/// 2400 → 10), точечные проверки формулы на известных числах (E по D=400, S 1/0/0.5, симметрия
/// дельт), 2v2 с неравными парами (средний рейтинг противников), обновление рейтинга и счётчиков
/// обеих пар, старт новичка из конфига, перезапись лестницы под ICCup одним конфигом, и интеграция
/// с сессией: политика выбирается при создании сессии (новый параметр фабрики, дефолт — прежняя
/// плоская схема ±15/−10, её поведение закреплено старыми тестами GameSessionTests).
/// </summary>
public class EloTests
{
    private static readonly PlayerId P1 = new("p1");
    private static readonly PlayerId P2 = new("p2");
    private static readonly PlayerId P3 = new("p3");
    private static readonly PlayerId P4 = new("p4");

    private static MovementConfig Cfg => MovementConfig.Default();

    // ---- Формула: ступени K и точные числа (E по D=400, S = 1/0/0.5) ----

    [Fact]
    public void Новичок_без_сыгранных_игр_ходит_сороковым_К_и_берёт_двадцать_за_решение_равных()
    {
        var policy = new EloPolicy(); // старт новичка — дефолт 0, как у RatingRepository (Points = 0)

        var updated = policy.Apply(Duel(P1, P2, RatingGameWinner.TeamA), Ratings());

        // E равных = 0.5, K новичка 40: шаг за победу = 40×(1−0.5) = 20
        var winner = updated.Single(r => r.Player == P1);
        Assert.Equal(20, winner.Points);
        Assert.Equal(1, winner.Wins);
        Assert.Equal(0, winner.Losses);
        var loser = updated.Single(r => r.Player == P2);
        Assert.Equal(-20, loser.Points);
        Assert.Equal(0, loser.Wins);
        Assert.Equal(1, loser.Losses);
    }

    [Fact]
    public void На_двадцати_девяти_игр_K_ещё_сорок_а_на_тридцати_уже_двадцать()
    {
        var policy = new EloPolicy();

        // 29 rated-игр (14 побед + 15 поражений) — ещё новичок: K = 40 → +20 за решение равных
        var at29 = policy.Apply(Duel(P1, P2, RatingGameWinner.TeamA), Ratings(
            new PlayerRating(P1, 1600, 14, 15),
            new PlayerRating(P2, 1600, 14, 15)));
        Assert.Equal(1620, at29.Single(r => r.Player == P1).Points);

        // ровно 30 rated-игр — порог пройден: K = 20 → +10
        var at30 = policy.Apply(Duel(P1, P2, RatingGameWinner.TeamA), Ratings(
            new PlayerRating(P1, 1600, 15, 15),
            new PlayerRating(P2, 1600, 15, 15)));
        Assert.Equal(1610, at30.Single(r => r.Player == P1).Points);
    }

    [Fact]
    public void На_рейтинге_2400_K_падает_до_десяти_а_на_2399_ещё_двадцать()
    {
        var policy = new EloPolicy();

        // 2399 при 200 rated-игр — ещё основной диапазон: K = 20 → +10 за решение равных
        var at2399 = policy.Apply(Duel(P1, P2, RatingGameWinner.TeamA), Ratings(
            new PlayerRating(P1, 2399, 100, 100),
            new PlayerRating(P2, 2399, 100, 100)));
        Assert.Equal(2409, at2399.Single(r => r.Player == P1).Points);

        // 2400 и выше — мастерский K = 10 → +5
        var at2400 = policy.Apply(Duel(P1, P2, RatingGameWinner.TeamA), Ratings(
            new PlayerRating(P1, 2400, 100, 100),
            new PlayerRating(P2, 2400, 100, 100)));
        Assert.Equal(2405, at2400.Single(r => r.Player == P1).Points);
    }

    [Fact]
    public void Ничья_равных_рейтингов_ничего_не_меняет_и_счётчиков_не_ведёт()
    {
        var policy = new EloPolicy();

        var updated = policy.Apply(Duel(P1, P2, RatingGameWinner.Draw), Ratings(
            new PlayerRating(P1, 1000, 0, 0),
            new PlayerRating(P2, 1000, 5, 7)));

        // S ничьей 0.5 = E равных → шаг ровно 0; ничья не растит ни Wins, ни Losses
        var first = updated.Single(r => r.Player == P1);
        Assert.Equal(1000, first.Points);
        Assert.Equal(0, first.Wins);
        Assert.Equal(0, first.Losses);
        var second = updated.Single(r => r.Player == P2);
        Assert.Equal(1000, second.Points);
        Assert.Equal(5, second.Wins);
        Assert.Equal(7, second.Losses);
    }

    [Fact]
    public void Ничья_слабого_против_сильного_слабому_выгодна_сильному_разорительна_и_симметрична()
    {
        var policy = new EloPolicy();

        var updated = policy.Apply(Duel(P1, P2, RatingGameWinner.Draw), Ratings(
            new PlayerRating(P1, 1000, 0, 0),
            new PlayerRating(P2, 1400, 0, 0)));

        // разброс ровно D=400: E слабого = 1/11, шаг 40×(0.5−1/11) = +16.36 → +16
        var weak = updated.Single(r => r.Player == P1);
        Assert.Equal(1016, weak.Points);
        Assert.True(weak.Points > 1000, "при ничьей E<S — слабый набирает");
        // E сильного = 10/11, шаг 40×(0.5−10/11) = −16.36 → −16
        var strong = updated.Single(r => r.Player == P2);
        Assert.Equal(1384, strong.Points);
        Assert.True(strong.Points < 1400, "при ничьей E>S — сильный теряет");

        // симметрия: дельты равны по модулю и противоположны по знаку
        Assert.Equal(16, Math.Abs(weak.Points - 1000));
        Assert.Equal(16, Math.Abs(1400 - strong.Points));
        Assert.Equal(weak.Points - 1000, 1400 - strong.Points);
    }

    [Fact]
    public void В_2v2_с_неравными_парами_считается_средний_рейтинг_противников()
    {
        var policy = new EloPolicy();
        // A: 1000 + 1000; B: 1000 + 1400; ничья
        var updated = policy.Apply(Game2v2(RatingGameWinner.Draw), Ratings(
            new PlayerRating(P1, 1000, 0, 0),
            new PlayerRating(P2, 1000, 0, 0),
            new PlayerRating(P3, 1000, 0, 0),
            new PlayerRating(P4, 1400, 0, 0)));

        // P1 и P3 в одном рейтинге 1000, но противники P1 — среднее (1000+1400)/2 = 1200,
        // противники P3 — среднее (1000+1000)/2 = 1000: при равных рейтингах шаги различаются
        var firstOfA = updated.Single(r => r.Player == P1);
        Assert.Equal(1010, firstOfA.Points); // 40×(0.5 − 1/(1+10^0.5)) = +10.39 → +10
        Assert.Equal(1010, updated.Single(r => r.Player == P2).Points);
        Assert.Equal(1000, updated.Single(r => r.Player == P3).Points); // E = S = 0.5 → 0
        Assert.Equal(1384, updated.Single(r => r.Player == P4).Points); // противники 1000/1000 → −16

        // то же очко против одиночного равного дало бы +16 (см. сценарий ничьей слабого с сильным),
        // а против усреднённой пары 1000/1400 — только +10: среднее противников учтено
        Assert.True(firstOfA.Points - 1000 < 16, "противники усреднены по всей противной стороне");
    }

    [Fact]
    public void Победа_в_2v2_обновляет_рейтинги_и_счётчики_обеих_пар()
    {
        var policy = new EloPolicy();

        var updated = policy.Apply(Game2v2(RatingGameWinner.TeamA), Ratings(
            new PlayerRating(P1, 1000, 0, 0),
            new PlayerRating(P2, 1000, 0, 0),
            new PlayerRating(P3, 1000, 0, 0),
            new PlayerRating(P4, 1000, 0, 0)));

        foreach (var winner in new[] { P1, P2 })
        {
            var rating = updated.Single(r => r.Player == winner);
            Assert.Equal(1020, rating.Points); // K новичка 40, S−E = 0.5 → +20
            Assert.Equal(1, rating.Wins);
            Assert.Equal(0, rating.Losses);
        }

        foreach (var loser in new[] { P3, P4 })
        {
            var rating = updated.Single(r => r.Player == loser);
            Assert.Equal(980, rating.Points);
            Assert.Equal(0, rating.Wins);
            Assert.Equal(1, rating.Losses);
        }
    }

    [Fact]
    public void Рейтинг_без_записи_в_словаре_считается_стартовым_конфига()
    {
        var policy = new EloPolicy(new EloConfig { StartRating = 1150 }); // дефолт переписан одной строкой

        var updated = policy.Apply(Duel(P1, P2, RatingGameWinner.Draw), Ratings()); // словарь пуст

        Assert.All(updated, r => Assert.Equal(1150, r.Points)); // новички стартуют по конфигу

        // дефолт-политика: старт 0 — фактически используемый старт RatingRepository
        var defaults = new EloPolicy().Apply(Duel(P1, P2, RatingGameWinner.TeamA), Ratings());
        Assert.Equal(20, defaults.Single(r => r.Player == P1).Points);
        Assert.Equal(-20, defaults.Single(r => r.Player == P2).Points);
    }

    [Fact]
    public void ICCup_лестница_задаётся_правкой_одного_конфига()
    {
        var policy = new EloPolicy(new EloConfig { D = 250, NoviceK = 25, NormalK = 25, MasterK = 25 });

        var updated = policy.Apply(Duel(P1, P2, RatingGameWinner.Draw), Ratings(
            new PlayerRating(P1, 1000, 0, 0),
            new PlayerRating(P2, 1500, 0, 0)));

        // разброс 500 при D=250 → E слабого = 1/101: 25×(0.5−1/101) = +12.25 → +12
        var weak = updated.Single(r => r.Player == P1);
        Assert.Equal(1012, weak.Points);
        Assert.True(weak.Points > 1000);
        Assert.Equal(1488, updated.Single(r => r.Player == P2).Points);
    }

    [Fact]
    public void Репозиторий_политики_трактует_дуэль_RecordOutcome_как_1v1_Эло()
    {
        var repo = new EloRatingRepository(new EloPolicy());

        Assert.Equal(0, repo.GetOrCreate(P1).Points); // новичок: старт дефолта — как у RatingRepository

        var (winner, loser) = repo.RecordOutcome(new BattleOutcome(P1, P2));

        Assert.Equal(20, winner.Points); // K=40, E равных = 0.5 → +20
        Assert.Equal(1, winner.Wins);
        Assert.Equal(-20, loser.Points);
        Assert.Equal(1, loser.Losses);
        Assert.Equal(20, repo.GetOrCreate(P1).Points); // Apply сохранён в хранилище политики
    }

    // ---- Сессия: политика выбирается при создании (новый параметр фабрики) ----

    private static GameSessionService NewSession(IRatingPolicy? ratingPolicy = null)
    {
        var rooms = new RoomService();
        var created = rooms.CreateRoom(P1);
        rooms.Join(created.Room.Id, P2);
        rooms.Join(created.Room.Id, P3);
        rooms.Join(created.Room.Id, P4);
        return GameSessionService.StartFromRoom(
            rooms, created.Room.Id, HostRuleSet.Default(), ratingPolicy: ratingPolicy);
    }

    private static ShipUnit Ship(string id, string @class)
    {
        var profile = TestBalance.Balance.ShipClasses.TryGetValue(@class, out var known)
            ? known
            : TestBalance.Balance.UnknownClassProfile;
        return new ShipUnit(new ShipId(id), @class, profile.Hull, profile.Shield);
    }

    private static void TickUntil(GameSessionService session, Func<bool> done, int maxTicks = 40)
    {
        for (var i = 0; i < maxTicks && !done(); i++) session.Tick(TimeSpan.FromSeconds(1));
        Assert.True(done(), $"условие не выполнилось за {maxTicks} тиков по 1 с");
    }

    [Fact]
    public void Эло_сессия_через_новый_параметр_фабрики_пишет_рейтинг_по_политике()
    {
        // старт 1000 из конфига, все новички (0 rated-игр): победа равных даёт ±K/2 = ±20
        var session = NewSession(new EloPolicy(new EloConfig { StartRating = 1000 }));
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetFleet(player, [Ship($"{player.Value}-s", "scout")]);
            session.ConfirmFleet(player);
        }

        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            session.SetPath(
                player, new ShipId($"{player.Value}-s"), [new Waypoint(Cfg.BasePoints[BaseId.Central])]);
        }

        TickUntil(session, () => session.IsOver, 30);
        Assert.Equal(SessionOutcome.TeamAWon, session.Outcome);

        foreach (var winner in new[] { P1, P2 })
        {
            var rating = session.Ratings.GetOrCreate(winner);
            Assert.Equal(1020, rating.Points); // по политике, а не плоские ±15
            Assert.Equal(1, rating.Wins);
            Assert.Equal(0, rating.Losses);
        }

        foreach (var loser in new[] { P3, P4 })
        {
            var rating = session.Ratings.GetOrCreate(loser);
            Assert.Equal(980, rating.Points);
            Assert.Equal(0, rating.Wins);
            Assert.Equal(1, rating.Losses);
        }
    }

    [Fact]
    public void Эло_сессия_при_ничьей_рейтинг_не_пишет()
    {
        var session = NewSession(new EloPolicy(new EloConfig { StartRating = 1000 }));
        session.SetFleet(P1, [Ship("p1-s", "scout")]); // лейн 1: A-скаут против B-тяжёлого
        session.SetFleet(P2, [Ship("p2-h", "heavy")]); // лейн 2: A-тяжёлый против B-скаута
        session.SetFleet(P3, [Ship("p3-h", "heavy")]);
        session.SetFleet(P4, [Ship("p4-s", "scout")]);
        foreach (var player in new[] { P1, P2, P3, P4 }) session.ConfirmFleet(player);
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            var target = player == P1 || player == P4 ? "s" : "h"; // каждому — его корабль
            session.SetPath(
                player, new ShipId($"{player.Value}-{target}"),
                [new Waypoint(Cfg.BasePoints[BaseId.Central])]);
        }

        TickUntil(session, () => session.IsOver, 30);

        // ничья рейтингом не пишется и при политике: старт конфига остаётся нетронутым
        Assert.Equal(SessionOutcome.Draw, session.Outcome);
        foreach (var player in new[] { P1, P2, P3, P4 })
        {
            var rating = session.Ratings.GetOrCreate(player);
            Assert.Equal(1000, rating.Points);
            Assert.Equal(0, rating.Wins);
            Assert.Equal(0, rating.Losses);
        }
    }

    // ---- Хелперы рейтинга ----

    private static IReadOnlyDictionary<PlayerId, PlayerRating> Ratings(params PlayerRating[] ratings) =>
        ratings.ToDictionary(r => r.Player);

    private static RatingGameResult Duel(PlayerId a, PlayerId b, RatingGameWinner winner) =>
        new([a], [b], winner);

    private static RatingGameResult Game2v2(RatingGameWinner winner) => new([P1, P2], [P3, P4], winner);
}
