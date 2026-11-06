namespace Rts.Domain.Tests;

/// <summary>
/// Сценарии сборки матча в бой: BothReady в лобби + подтверждённые флоты → MatchStarted
/// и бой, стартующий через CombatService.Start.
/// </summary>
public class MatchOrchestratorTests
{
    private static ShipUnit Ship(string id) => new(new ShipId(id), "liner", 100, 0);

    /// <summary>Лобби, дошедшее до BothReady, и его матч.</summary>
    private static (IMatchmakingService Lobby, Match Match) BothReadyLobby()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);
        lobby.Join(new PlayerId("a"));
        var match = lobby.Join(new PlayerId("b"));
        Assert.NotNull(match);
        lobby.Ready(match!.A);
        lobby.Ready(match.B);
        return (lobby, match);
    }

    /// <summary>Оркестратор над сервисами из фабрики, к нему прикреплён матч.</summary>
    private static IMatchOrchestrator Attached(IMatchmakingService lobby, Match match)
    {
        var orchestrator = TestDomain.Factory.CreateOrchestrator(
            lobby,
            TestDomain.Factory.CreateFleetRegistry(),
            TestDomain.Factory.CreateCombat(TestBalance.Balance));
        orchestrator.Attach(match);
        return orchestrator;
    }

    /// <summary>BothReady, составы выставлены и подтверждены — без старта (нужен второй ConfirmFleet-триггер).</summary>
    private static (IMatchmakingService Lobby, Match Match, IMatchOrchestrator Orchestrator)
        FleetsConfirmedWithoutStart()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);
        lobby.Join(new PlayerId("a"));
        var match = lobby.Join(new PlayerId("b"));
        Assert.NotNull(match);
        lobby.Ready(match!.B); // пока только один Ready — BothReady ещё нет
        var orchestrator = Attached(lobby, match);
        orchestrator.SetFleet(match.Id, match.A, new[] { Ship("a1") });
        orchestrator.SetFleet(match.Id, match.B, new[] { Ship("b1") });

        Assert.Null(orchestrator.ConfirmFleet(match.Id, match.A));
        Assert.Null(orchestrator.ConfirmFleet(match.Id, match.B)); // флоты оба подтверждены, лобби — нет
        Assert.True(orchestrator.AllFleetsConfirmed(match.Id));
        Assert.Null(orchestrator.TryGetBattle(match.Id));

        return (lobby, match, orchestrator);
    }

    /// <summary>Полный путь до старта: BothReady, составы выставлены.</summary>
    private static (IMatchOrchestrator Orchestrator, Match Match) ReadyWithFleetSet()
    {
        var (lobby, match) = BothReadyLobby();
        var orchestrator = Attached(lobby, match);
        orchestrator.SetFleet(match.Id, match.A, new[] { Ship("a1") });
        orchestrator.SetFleet(match.Id, match.B, new[] { Ship("b1") });
        return (orchestrator, match);
    }

    [Fact]
    public void Оба_Ready_и_оба_флота_подтверждены_матч_стартует()
    {
        var (orchestrator, match) = ReadyWithFleetSet();

        Assert.Null(orchestrator.ConfirmFleet(match.Id, match.A)); // подтверждён один — старт ещё рано
        var started = orchestrator.ConfirmFleet(match.Id, match.B);

        Assert.NotNull(started);
        Assert.Equal(match.Id, started!.Match);
        Assert.Equal(match.A, started.PlayerA);
        Assert.Equal(match.B, started.PlayerB);
        Assert.NotNull(started.Battle);
        Assert.Same(started.Battle, orchestrator.TryGetBattle(match.Id));
    }

    [Fact]
    public void Один_флот_не_подтверждён_боя_нет()
    {
        var (orchestrator, match) = ReadyWithFleetSet();

        orchestrator.ConfirmFleet(match.Id, match.A); // подтвердил состав только игрок A

        Assert.False(orchestrator.AllFleetsConfirmed(match.Id));
        Assert.Null(orchestrator.TryGetBattle(match.Id));
    }

    [Fact]
    public void Флоты_подтверждены_до_BothReady_бой_не_стартует()
    {
        FleetsConfirmedWithoutStart();
    }

    [Fact]
    public void BothReady_после_подтверждённых_флотов_TryStartBattle_стартует()
    {
        var (lobby, match, orchestrator) = FleetsConfirmedWithoutStart();

        lobby.Ready(match.A);
        lobby.Ready(match.B);

        var started = orchestrator.TryStartBattle(match.Id);

        Assert.NotNull(started);
        Assert.Equal(match.Id, started!.Match);
        Assert.NotNull(orchestrator.TryGetBattle(match.Id));
    }

    [Fact]
    public void Повторное_подтверждение_флота_не_создаёт_второй_бой()
    {
        var (orchestrator, match) = ReadyWithFleetSet();
        orchestrator.ConfirmFleet(match.Id, match.A);
        orchestrator.ConfirmFleet(match.Id, match.B);
        var battle = orchestrator.TryGetBattle(match.Id);
        Assert.NotNull(battle);

        orchestrator.SetFleet(match.Id, match.B, new[] { Ship("b2") }); // правка состава после старта
        var again = orchestrator.ConfirmFleet(match.Id, match.B);

        Assert.Null(again); // второй MatchStarted не появляется
        Assert.Same(battle, orchestrator.TryGetBattle(match.Id)); // бой тот же самый
        // состав текущего боя не тронут: в нём прежний флот B
        Assert.Equal("b1", battle!.TeamB.Ships[0].Id.Value);
    }

    [Fact]
    public void Бой_стартует_на_флотах_из_реестра_и_тик_наносит_урон()
    {
        var (orchestrator, match) = ReadyWithFleetSet();
        orchestrator.ConfirmFleet(match.Id, match.A);
        orchestrator.ConfirmFleet(match.Id, match.B);
        var battle = orchestrator.TryGetBattle(match.Id);
        Assert.NotNull(battle);

        // бои по тикам ведёт тот же CombatService: флоты оркестратора — флоты реестра
        var combat = TestDomain.Factory.CreateCombat(TestBalance.Balance);
        var tick = combat.Tick(battle!);

        Assert.Equal(90, tick.Battle.TeamA.Ships[0].Hull); // щит 0 → урон 10 ушёл в корпус
        Assert.Equal(90, tick.Battle.TeamB.Ships[0].Hull);
    }

    [Fact]
    public void Неприкреплённый_матч_не_собирается_и_не_стартует()
    {
        var lobby = TestDomain.Factory.CreateLobby(TestBalance.Balance);
        lobby.Join(new PlayerId("a"));
        var match = lobby.Join(new PlayerId("b"));
        Assert.NotNull(match);
        lobby.Ready(match!.A);
        lobby.Ready(match.B);
        var orchestrator = TestDomain.Factory.CreateOrchestrator(
            lobby,
            TestDomain.Factory.CreateFleetRegistry(),
            TestDomain.Factory.CreateCombat(TestBalance.Balance)); // Attach не вызван

        Assert.Throws<InvalidOperationException>(() => orchestrator.ConfirmFleet(match.Id, match.A));
        Assert.Throws<InvalidOperationException>(() => orchestrator.TryStartBattle(match.Id));
        Assert.Throws<InvalidOperationException>(() => orchestrator.AllFleetsConfirmed(match.Id));
        Assert.Null(orchestrator.TryGetBattle(match.Id));
    }
}
