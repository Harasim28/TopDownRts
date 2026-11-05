namespace Rts.Domain.Tests;

/// <summary>
/// Кадры 2+4a: правила режима, которые хост задаёт ползунками (HostRuleSet), и базовый захват
/// (BaseControlService): три базы, владение A/B/нейтральное, захват по присутствию, очки за
/// удержание по профилю базы, таймер непрерывного владения всеми базами. Дефолты — по аналогии
/// с доминацией/завоеванием (WarThunder), НЕ подтверждены владельцем, кроме 10 минут удержания
/// (пример владельца от 05.11).
/// </summary>
public class ModeTests
{
    private static readonly Team TeamA = new(0);
    private static readonly Team TeamB = new(1);

    private static IBaseControlService NewMatch() => new BaseControlService(HostRuleSet.Default());

    private static TimeSpan Secs(int seconds) => TimeSpan.FromSeconds(seconds);
    private static TimeSpan Mins(int minutes) => TimeSpan.FromMinutes(minutes);

    private static BasePresence At(BaseId baseId, Team team, int aliveShips) => new(baseId, team, aliveShips);

    private static void Set(IBaseControlService match, params BasePresence[] presence) => match.SetPresence(presence);

    /// <summary>A забирает центр и вражеский дом (по одному 20-секундному тику); свой дом у A с начала матча.</summary>
    private static void TeamACapturesEverything(IBaseControlService match)
    {
        Set(match, At(BaseId.Central, TeamA, 1));
        match.Tick(Secs(20));
        Set(match, At(BaseId.HomeB, TeamA, 1));
        match.Tick(Secs(20));
    }

    [Fact]
    public void Дефолтный_набор_правил_лежит_в_диапазонах_своих_слайдеров()
    {
        var rules = HostRuleSet.Default();

        Assert.Equal(10, rules.HoldAllBasesMinutes);
        Assert.Equal(600, rules.VictoryScoreThreshold);
        Assert.Equal(30, rules.OwnBaseScorePerMinute);
        Assert.Equal(60, rules.CentralBaseScorePerMinute);
        Assert.Equal(30, rules.EnemyBaseScorePerMinute);
        Assert.Equal(20, rules.CaptureSeconds);
        Assert.Equal(Mode.BaseControl, rules.Mode);

        Assert.InRange(rules.HoldAllBasesMinutes, HostRuleSet.HoldAllBasesMinutesSlider.Min, HostRuleSet.HoldAllBasesMinutesSlider.Max);
        Assert.InRange(rules.VictoryScoreThreshold, HostRuleSet.VictoryScoreThresholdSlider.Min, HostRuleSet.VictoryScoreThresholdSlider.Max);
        Assert.InRange(rules.OwnBaseScorePerMinute, HostRuleSet.OwnBaseScorePerMinuteSlider.Min, HostRuleSet.OwnBaseScorePerMinuteSlider.Max);
        Assert.InRange(rules.CentralBaseScorePerMinute, HostRuleSet.CentralBaseScorePerMinuteSlider.Min, HostRuleSet.CentralBaseScorePerMinuteSlider.Max);
        Assert.InRange(rules.EnemyBaseScorePerMinute, HostRuleSet.EnemyBaseScorePerMinuteSlider.Min, HostRuleSet.EnemyBaseScorePerMinuteSlider.Max);
        Assert.InRange(rules.CaptureSeconds, HostRuleSet.CaptureSecondsSlider.Min, HostRuleSet.CaptureSecondsSlider.Max);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(61)]
    public void Минуты_удержания_вне_диапазона_слайдера_отклоняются(int holdAllBasesMinutes)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new HostRuleSet(holdAllBasesMinutes: holdAllBasesMinutes));

        Assert.Contains("holdAllBasesMinutes", ex.Message);
    }

    [Fact]
    public void Порог_очков_и_секунды_захвата_вне_диапазонов_тоже_отклоняются()
    {
        Assert.Throws<InvalidOperationException>(() => new HostRuleSet(victoryScoreThreshold: 99));
        Assert.Throws<InvalidOperationException>(() => new HostRuleSet(victoryScoreThreshold: 2001));
        Assert.Throws<InvalidOperationException>(() => new HostRuleSet(captureSeconds: 4));
        Assert.Throws<InvalidOperationException>(() => new HostRuleSet(captureSeconds: 61));
    }

    [Fact]
    public void Стартовая_расстановка_симметрична_а_центр_нейтрален()
    {
        var match = NewMatch();

        Assert.Equal((Team?)TeamA, match.OwnerOf(BaseId.HomeA));
        Assert.Equal((Team?)TeamB, match.OwnerOf(BaseId.HomeB));
        Assert.Null(match.OwnerOf(BaseId.Central));
        Assert.Equal(0, match.ScoreOf(TeamA), 6);
        Assert.Equal(0, match.ScoreOf(TeamB), 6);
        Assert.False(match.IsOver);
        Assert.Null(match.Winner);
    }

    [Fact]
    public void Захват_нейтральной_базы_по_непрерывному_присутствию()
    {
        var match = NewMatch();

        Set(match, At(BaseId.Central, TeamA, 2)); // два живых корабля A в центре
        match.Tick(Secs(15));

        Assert.Null(match.OwnerOf(BaseId.Central)); // 15 < 20 секунд — захват ещё не готов
        Assert.Equal((Team?)TeamA, match.CapturingTeamOf(BaseId.Central));
        Assert.Equal(15, match.CaptureProgressSeconds(BaseId.Central), 6);

        match.Tick(Secs(5)); // суммарно 20 секунд непрерывно

        Assert.Equal((Team?)TeamA, match.OwnerOf(BaseId.Central));
        Assert.Equal(0, match.CaptureProgressSeconds(BaseId.Central), 6);
        Assert.Null(match.CapturingTeamOf(BaseId.Central));
    }

    [Fact]
    public void Захват_вражеского_дома_меняет_владельца()
    {
        var match = NewMatch();

        Set(match, At(BaseId.HomeA, TeamB, 1));
        match.Tick(Secs(20)); // ровно CaptureSeconds непрерывно

        Assert.Equal((Team?)TeamB, match.OwnerOf(BaseId.HomeA));
    }

    [Fact]
    public void Оспаривание_и_пустая_зона_сбрасывают_прогресс_захвата()
    {
        var match = NewMatch();

        Set(match, At(BaseId.Central, TeamA, 1));
        match.Tick(Secs(10));
        Assert.Equal(10, match.CaptureProgressSeconds(BaseId.Central), 6);

        Set(match, At(BaseId.Central, TeamA, 1), At(BaseId.Central, TeamB, 1)); // зона оспорена
        match.Tick(Secs(10));
        Assert.Equal(0, match.CaptureProgressSeconds(BaseId.Central), 6);
        Assert.Null(match.CapturingTeamOf(BaseId.Central));

        Set(match, At(BaseId.Central, TeamA, 1));
        match.Tick(Secs(10)); // пришлось копить заново
        Assert.Null(match.OwnerOf(BaseId.Central));
        Assert.Equal(10, match.CaptureProgressSeconds(BaseId.Central), 6);

        Set(match); // зона пуста
        match.Tick(Secs(10));
        Assert.Equal(0, match.CaptureProgressSeconds(BaseId.Central), 6);
    }

    [Fact]
    public void Удержание_только_своей_базы_таймер_победы_не_копит()
    {
        var match = NewMatch(); // свои базы у A и B с начала, центр нейтрален

        match.Tick(Mins(11)); // больше HoldAllBasesMinutes, но все три базы не у одной команды

        Assert.False(match.IsOver);
        Assert.Null(match.Winner);
        Assert.Equal(0, match.HoldAllSecondsOf(TeamA), 6);
        Assert.Equal(0, match.HoldAllSecondsOf(TeamB), 6);
        // очки за свою базу капают, но до порога далеко: 30/мин × 11 = 330 < 600
        Assert.Equal(330, match.ScoreOf(TeamA), 6);
    }

    [Fact]
    public void Удержание_всех_трёх_баз_десять_минут_даёт_победу_по_таймеру()
    {
        // порог очков задран до максимума, чтобы победа пришла именно по таймеру
        var match = new BaseControlService(new HostRuleSet(victoryScoreThreshold: 2000));

        TeamACapturesEverything(match);
        match.Tick(Mins(9));
        Assert.False(match.IsOver);
        Assert.Equal(540, match.HoldAllSecondsOf(TeamA), 6);

        match.Tick(Mins(1)); // суммарно ровно 10 минут владения всеми тремя

        Assert.True(match.IsOver);
        Assert.Equal((Team?)TeamA, match.Winner);
        Assert.Equal(600, match.HoldAllSecondsOf(TeamA), 6);
    }

    [Fact]
    public void Порог_очков_срабатывает_раньше_таймера_всех_баз()
    {
        var match = NewMatch();

        Set(match, At(BaseId.Central, TeamA, 1));
        match.Tick(Secs(20)); // A забрал центр
        match.Tick(Mins(10)); // A: своя 30 + центральная 60 = 90/мин → 910 очков ≥ 600

        Assert.True(match.IsOver);
        Assert.Equal((Team?)TeamA, match.Winner);
        Assert.Equal(0, match.HoldAllSecondsOf(TeamA), 6); // всех трёх баз у A не было — таймер не копился
    }

    [Fact]
    public void После_победы_очки_таймер_и_захват_не_растут()
    {
        var match = NewMatch();

        Set(match, At(BaseId.Central, TeamA, 1));
        match.Tick(Secs(20));
        match.Tick(Mins(10)); // победа A по порогу очков (910 ≥ 600)
        Assert.True(match.IsOver);

        var scoreA = match.ScoreOf(TeamA);
        var holdA = match.HoldAllSecondsOf(TeamA);

        Set(match, At(BaseId.Central, TeamB, 1)); // B пытается перехватить — матч уже окончен
        match.Tick(Mins(5));

        Assert.True(match.IsOver);
        Assert.Equal((Team?)TeamA, match.Winner);
        Assert.Equal(scoreA, match.ScoreOf(TeamA), 6);
        Assert.Equal(holdA, match.HoldAllSecondsOf(TeamA), 6);
        Assert.Equal((Team?)TeamA, match.OwnerOf(BaseId.Central)); // захват не состоялся
        Assert.Null(match.CapturingTeamOf(BaseId.Central));
    }

    [Fact]
    public void Очки_за_минуту_соответствуют_профилю_своя_центральная_вражеская()
    {
        var match = NewMatch(); // у A свой дом с начала: своя база 30/мин

        Set(match, At(BaseId.Central, TeamA, 1));
        match.Tick(Secs(20)); // захват центра; за тик набрана только своя база: 30 × ⅓ минуты
        Assert.Equal(10, match.ScoreOf(TeamA), 6);

        match.Tick(Mins(1)); // своя 30 + центральная 60
        Assert.Equal(100, match.ScoreOf(TeamA), 6);

        Set(match, At(BaseId.HomeB, TeamA, 1));
        match.Tick(Secs(20)); // захват вражеского дома; за тик своя+центральная: 90 × ⅓ минуты
        Assert.Equal(130, match.ScoreOf(TeamA), 6);

        match.Tick(Mins(1)); // своя 30 + центральная 60 + вражеская 30
        Assert.Equal(250, match.ScoreOf(TeamA), 6);
    }

    [Fact]
    public void Таймер_всех_баз_сбрасывается_при_потере_одной_из_баз()
    {
        var match = new BaseControlService(new HostRuleSet(victoryScoreThreshold: 2000));

        TeamACapturesEverything(match);
        match.Tick(Mins(9)); // 9 минут подряд владеет всеми тремя
        Assert.Equal(540, match.HoldAllSecondsOf(TeamA), 6);

        Set(match, At(BaseId.Central, TeamB, 1));
        match.Tick(Secs(20)); // B отбил центр — серия владения прервана
        Assert.Equal(0, match.HoldAllSecondsOf(TeamA), 6);

        Set(match, At(BaseId.Central, TeamA, 1));
        match.Tick(Secs(20)); // A вернул центр — серия начнётся заново со следующего тика
        Assert.Equal(0, match.HoldAllSecondsOf(TeamA), 6);

        match.Tick(Mins(1)); // снова владеет всеми тремя полную минуту
        Assert.Equal(60, match.HoldAllSecondsOf(TeamA), 6);
        Assert.False(match.IsOver); // и 10 минут не набралось, и очки до порога не дошли
    }
}
