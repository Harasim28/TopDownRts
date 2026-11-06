namespace Rts.Domain.Tests;

public class RatingTests
{
    [Fact]
    public void Новый_игрок_рейтинг_нулевой()
    {
        var rating = TestDomain.Factory.CreateRating(TestBalance.Balance);

        var profile = rating.GetOrCreate(new PlayerId("a"));

        Assert.Equal(0, profile.Points);
        Assert.Equal(0, profile.Wins);
        Assert.Equal(0, profile.Losses);
    }

    [Fact]
    public void Исход_боя_начисляет_очки_из_конфига_и_счётчики()
    {
        var rating = TestDomain.Factory.CreateRating(TestBalance.Balance);

        var (winner, loser) = rating.RecordOutcome(
            new BattleOutcome(new PlayerId("a"), new PlayerId("b")));

        Assert.Equal(TestBalance.Balance.RatingWinPoints, winner.Points);   // +15
        Assert.Equal(1, winner.Wins);
        Assert.Equal(0, winner.Losses);
        Assert.Equal(-TestBalance.Balance.RatingLosePoints, loser.Points);  // −10
        Assert.Equal(0, loser.Wins);
        Assert.Equal(1, loser.Losses);
    }

    [Fact]
    public void Накопление_нескольких_боёв()
    {
        var rating = TestDomain.Factory.CreateRating(TestBalance.Balance);
        rating.RecordOutcome(new BattleOutcome(new PlayerId("a"), new PlayerId("b")));
        rating.RecordOutcome(new BattleOutcome(new PlayerId("b"), new PlayerId("a")));

        var a = rating.GetOrCreate(new PlayerId("a"));
        var b = rating.GetOrCreate(new PlayerId("b"));

        Assert.Equal(15 - 10, a.Points); // победа + поражение
        Assert.Equal(1, a.Wins);
        Assert.Equal(1, a.Losses);
        // b: поражение (−10) затем победа (+15)
        Assert.Equal(5, b.Points);
        Assert.Equal(1, b.Wins);
        Assert.Equal(1, b.Losses);
    }

    [Fact]
    public void Свои_числа_конфига_в_начислении()
    {
        var rating = TestDomain.Factory.CreateRating(new BalanceConfig
        {
            RatingWinPoints = 7,
            RatingLosePoints = 3,
        });

        var (winner, loser) = rating.RecordOutcome(
            new BattleOutcome(new PlayerId("x"), new PlayerId("y")));

        Assert.Equal(7, winner.Points);
        Assert.Equal(-3, loser.Points);
    }
}