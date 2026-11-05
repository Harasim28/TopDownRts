namespace Rts.Domain.Tests;

public class CombatTests
{
    private static ShipUnit Ship(string id, int hull, int shield) =>
        new(new ShipId(id), "linac", hull, shield);

    private static Fleet Fleet(string player, Team team, params ShipUnit[] ships) =>
        new(new PlayerId(player), team, ships);

    [Fact]
    public void Реестр_флотов_запоминает_и_подтверждает()
    {
        var fleets = TestDomain.Factory.CreateFleetRegistry();
        var matchId = new MatchId("m1");
        var ships = new ShipUnit[] { Ship("s1", 100, 50) };

        fleets.SetFleet(matchId, new PlayerId("a"), ships);
        Assert.False(fleets.IsConfirmed(matchId, new PlayerId("a")));
        Assert.NotNull(fleets.TryGetFleet(matchId, new PlayerId("a")));

        fleets.ConfirmFleet(matchId, new PlayerId("a"));
        Assert.True(fleets.IsConfirmed(matchId, new PlayerId("a")));
    }

    [Fact]
    public void Начало_боя_оба_флота_живы()
    {
        var combat = TestDomain.Factory.CreateCombat(TestBalance.Balance);
        var a = Fleet("a", new Team(0), Ship("s1", 100, 0));
        var b = Fleet("b", new Team(1), Ship("s2", 100, 10));
        var battle = combat.Start(new MatchId("m1"), a, b);

        Assert.False(battle.IsOver);
        Assert.False(battle.TeamA.IsWiped);
        Assert.False(battle.TeamB.IsWiped);
    }

    [Fact]
    public void Тик_щит_гасит_часть_урона()
    {
        var combat = TestDomain.Factory.CreateCombat(TestBalance.Balance);
        var a = Fleet("a", new Team(0), Ship("s1", 100, 0));
        var b = Fleet("b", new Team(1), Ship("s2", 100, 10));
        var battle = combat.Start(new MatchId("m1"), a, b);

        var tick = combat.Tick(battle);

        // урон 10; щит гасит первые 6 → щит 10-6=4; остаток урона 4 в корпус → hull 96
        Assert.Equal(96, tick.Battle.TeamB.Ships[0].Hull);
        Assert.Equal(4, tick.Battle.TeamB.Ships[0].Shield);
        Assert.Equal(4, tick.HullDamageToTeamB); // урон в корпус (после щита)
        // обратный огонь: у команды A щит 0 → полный урон 10 в корпус
        Assert.Equal(90, tick.Battle.TeamA.Ships[0].Hull);
        Assert.Equal(10, tick.HullDamageToTeamA);
    }

    [Fact]
    public void Тик_пустой_щит_нет_ущерба()
    {
        var combat = TestDomain.Factory.CreateCombat(TestBalance.Balance);
        var a = Fleet("a", new Team(0), Ship("s1", 100, 0));
        var b = Fleet("b", new Team(1), Ship("s2", 100, 0));
        var battle = combat.Start(new MatchId("m1"), a, b);

        var tick = combat.Tick(battle);

        // щит 0 → весь урон в корпус: у обоих hull 100-10=90
        Assert.Equal(90, tick.Battle.TeamA.Ships[0].Hull);
        Assert.Equal(90, tick.Battle.TeamB.Ships[0].Hull);
    }

    [Fact]
    public void Гибель_корабля_разгром_и_конец_боя()
    {
        var combat = TestDomain.Factory.CreateCombat(TestBalance.Balance);
        var a = Fleet("a", new Team(0), Ship("s1", 100, 30));
        var b = Fleet("b", new Team(1), Ship("s2", 10, 0));
        var battle = combat.Start(new MatchId("m1"), a, b);

        for (var i = 0; i < 10 && !battle.IsOver; i++)
        {
            battle = combat.Tick(battle).Battle;
        }

        Assert.True(battle.IsOver);
        Assert.True(battle.TeamB.IsWiped);
        Assert.False(battle.TeamA.IsWiped); // победа команды A
    }

    [Fact]
    public void Два_корабля_урон_распределяется_по_живым()
    {
        var combat = TestDomain.Factory.CreateCombat(TestBalance.Balance);
        var a = Fleet("a", new Team(0), Ship("s1", 100, 0));
        var b = Fleet("b", new Team(1), Ship("s2", 100, 0), Ship("s3", 100, 0));
        var battle = combat.Start(new MatchId("m1"), a, b);

        var tick = combat.Tick(battle);

        // урон 10 нацелен на ПЕРВЫЙ живой корабль команды B → hull 90, второй цел
        Assert.Equal(90, tick.Battle.TeamB.Ships[0].Hull);
        Assert.Equal(100, tick.Battle.TeamB.Ships[1].Hull);
    }
}