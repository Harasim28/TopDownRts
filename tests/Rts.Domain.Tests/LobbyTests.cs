namespace Rts.Domain.Tests;

/// <summary>Числа лобби — свой конфиг (дефолты завтрашних правок не трогают тесты).</summary>
internal static class TestBalance
{
    public static readonly BalanceConfig Balance = new()
    {
        ReadyTimeout = TimeSpan.FromSeconds(30),
        BaseDamagePerTick = 10,
        ShieldAbsorbsPerTick = 6,
        RatingWinPoints = 15,
        RatingLosePoints = 10,
    };
}

internal static class TestDomain
{
    public static readonly IDomainFactory Factory = new InMemoryDomainFactory();
}

public class LobbyTests
{
    private static Match JoinPair(IMatchmakingService lobby)
    {
        lobby.Join(new PlayerId("a"));
        var match = lobby.Join(new PlayerId("b"));
        Assert.NotNull(match);
        return match!;
    }

    [Fact]
    public void Двое_в_очереди_собирают_матч()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);

        var match = JoinPair(lobby);

        Assert.Equal(ReadyState.WaitingReady, match.State);
        Assert.Same(match, lobby.TryGetMatch(new PlayerId("a")));
        Assert.Same(match, lobby.TryGetMatch(new PlayerId("b")));
    }

    [Fact]
    public void Обычный_одиночный_вход_без_матча()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);

        var match = lobby.Join(new PlayerId("a"));

        Assert.Null(match);
        Assert.Null(lobby.TryGetMatch(new PlayerId("a")));
    }

    [Fact]
    public void Оба_Ready_у_матча()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);
        var match = JoinPair(lobby);

        lobby.Ready(match.A);
        lobby.Ready(match.B);

        Assert.Equal(ReadyState.BothReady, lobby.TryGetMatch(match.B)!.State);
    }

    [Fact]
    public void Таймаут_Ready_сбрасывает_лобби_и_очередь()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);
        var match = JoinPair(lobby);

        lobby.Ready(match.A);
        var timedOut = lobby.Tick(TestBalance.Balance.ReadyTimeout + TimeSpan.FromSeconds(1));

        var entry = Assert.Single(timedOut);
        Assert.Equal(match.Id, entry.Id); // сброшен именнно этот матч
        Assert.Equal(ReadyState.TimedOut, entry.State);
        Assert.Null(lobby.TryGetMatch(match.A));
        Assert.Null(lobby.TryGetMatch(match.B));
    }

    [Fact]
    public void Готовый_матч_таймаутом_не_сбрасывается()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);
        var match = JoinPair(lobby);
        lobby.Ready(match.A);
        lobby.Ready(match.B);

        var timedOut = lobby.Tick(TestBalance.Balance.ReadyTimeout + TimeSpan.FromSeconds(1));

        Assert.Empty(timedOut);
        Assert.Equal(ReadyState.BothReady, lobby.TryGetMatch(match.A)!.State);
    }

    [Fact]
    public void Отказ_одного_освобождает_обоих()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);
        var match = JoinPair(lobby);

        lobby.Decline(match.B);

        Assert.Null(lobby.TryGetMatch(match.A));
        Assert.Null(lobby.TryGetMatch(match.B));
    }
}