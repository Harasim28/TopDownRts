namespace Rts.Domain.Tests;

/// <summary>
/// Сценарии 3b: классы кораблей и капа закупа.
/// Спец-роли (поддержка/снабжение/разведка) в бою НЕ внедрены (не подтверждены владельцем) —
/// класс пока задаёт ТОЛЬКО профиль закупа: стоимость, корпус, щит.
/// </summary>
public class ShipTests
{
    private static BalanceConfig Balance => TestBalance.Balance;

    /// <summary>Корабль известного класса: корпус и щит берутся из профиля класса (числа — из конфига).</summary>
    private static ShipUnit Ship(string id, string @class)
    {
        var profile = Balance.ShipClasses[@class];
        return new ShipUnit(new ShipId(id), @class, profile.Hull, profile.Shield);
    }

    /// <summary>Корабль НЕизвестного класса: корпус/щит по дефолтному профилю, класс — как в старых тестах.</summary>
    private static ShipUnit Linac(string id) =>
        new(new ShipId(id), "linac", Balance.UnknownClassProfile.Hull, Balance.UnknownClassProfile.Shield);

    private static ShipUnit[] Repeat(string @class, int count) =>
        Enumerable.Range(0, count).Select(i => Ship($"{@class}{i}", @class)).ToArray();

    private static ShipUnit[] RepeatUnknown(int count) =>
        Enumerable.Range(0, count).Select(i => Linac($"linac{i}")).ToArray();

    private static ShipUnit[] Compose(params ShipUnit[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Профили_шести_классов_дают_числа_закупа()
    {
        Assert.Equal(6, Balance.ShipClasses.Count);

        Assert.Equal(5, Balance.ShipClasses["light"].Cost);
        Assert.Equal(50, Balance.ShipClasses["light"].Hull);
        Assert.Equal(20, Balance.ShipClasses["light"].Shield);

        Assert.Equal(8, Balance.ShipClasses["medium"].Cost);
        Assert.Equal(80, Balance.ShipClasses["medium"].Hull);
        Assert.Equal(40, Balance.ShipClasses["medium"].Shield);

        Assert.Equal(12, Balance.ShipClasses["heavy"].Cost);
        Assert.Equal(150, Balance.ShipClasses["heavy"].Hull);
        Assert.Equal(80, Balance.ShipClasses["heavy"].Shield);

        Assert.Equal(10, Balance.ShipClasses["support"].Cost);
        Assert.Equal(70, Balance.ShipClasses["support"].Hull);
        Assert.Equal(50, Balance.ShipClasses["support"].Shield);

        Assert.Equal(4, Balance.ShipClasses["scout"].Cost);
        Assert.Equal(30, Balance.ShipClasses["scout"].Hull);
        Assert.Equal(10, Balance.ShipClasses["scout"].Shield);

        Assert.Equal(9, Balance.ShipClasses["supply"].Cost);
        Assert.Equal(60, Balance.ShipClasses["supply"].Hull);
        Assert.Equal(30, Balance.ShipClasses["supply"].Shield);
    }

    [Fact]
    public void Состав_из_классов_в_пределах_капы_принимается()
    {
        var fleets = TestDomain.Factory.CreateFleetRegistry(Balance);
        var matchId = new MatchId("m1");
        var player = new PlayerId("a");
        var ships = Compose(Repeat("medium", 3), Repeat("light", 1), Repeat("scout", 1)); // 24+5+4=33 < 100

        fleets.SetFleet(matchId, player, ships);

        var fleet = fleets.TryGetFleet(matchId, player);
        Assert.NotNull(fleet);
        Assert.Equal(5, fleet!.Ships.Count);
        Assert.Equal("medium", fleet.Ships[0].Class);
    }

    [Fact]
    public void Ровно_на_границе_сто_очков_принимается()
    {
        var fleets = TestDomain.Factory.CreateFleetRegistry(Balance);
        var matchId = new MatchId("m1");
        var player = new PlayerId("a");
        var ships = Compose(Repeat("support", 2), Repeat("medium", 10)); // 2*10+10*8 = ровно 100

        fleets.SetFleet(matchId, player, ships);

        Assert.NotNull(fleets.TryGetFleet(matchId, player));
    }

    [Fact]
    public void Превышение_капы_на_очко_отклоняется()
    {
        var fleets = TestDomain.Factory.CreateFleetRegistry(Balance);
        var matchId = new MatchId("m1");
        var player = new PlayerId("a");
        var ships = Compose(Repeat("medium", 12), Repeat("light", 1)); // 96+5 = 101 > 100

        Assert.Throws<InvalidOperationException>(() => fleets.SetFleet(matchId, player, ships));

        Assert.Null(fleets.TryGetFleet(matchId, player)); // отказ — состав не запоминается
    }

    [Fact]
    public void Капа_независима_у_двух_игроков_одного_матча()
    {
        var fleets = TestDomain.Factory.CreateFleetRegistry(Balance);
        var matchId = new MatchId("m1");
        var a = new PlayerId("a");
        var b = new PlayerId("b");
        var full = Compose(Repeat("support", 2), Repeat("medium", 10)); // по 100 каждому

        fleets.SetFleet(matchId, a, full);
        fleets.SetFleet(matchId, b, full);
        fleets.ConfirmFleet(matchId, a);

        // попытка b залезть за капу отклоняется и не задевает ни a, ни прежний состав b
        Assert.Throws<InvalidOperationException>(() =>
            fleets.SetFleet(matchId, b, Compose(Repeat("medium", 12), Repeat("light", 1))));

        Assert.True(fleets.IsConfirmed(matchId, a)); // подтверждение a не сброшено чужим отказом
        Assert.Equal(12, fleets.TryGetFleet(matchId, b)!.Ships.Count); // у b остался прежний состав на 100
    }

    [Fact]
    public void Замена_флота_на_более_дорогой_после_подтверждения_сбрасывает_подтверждение()
    {
        var fleets = TestDomain.Factory.CreateFleetRegistry(Balance);
        var matchId = new MatchId("m1");
        var player = new PlayerId("a");

        fleets.SetFleet(matchId, player, Repeat("light", 1)); // 5 очков
        fleets.ConfirmFleet(matchId, player);
        Assert.True(fleets.IsConfirmed(matchId, player));

        fleets.SetFleet(matchId, player, Repeat("heavy", 2)); // 24 очка — дороже, но в капе

        Assert.False(fleets.IsConfirmed(matchId, player)); // правка состава требует повторного подтверждения
        Assert.Equal("heavy", fleets.TryGetFleet(matchId, player)!.Ships[0].Class);

        fleets.ConfirmFleet(matchId, player);
        Assert.True(fleets.IsConfirmed(matchId, player));
    }

    [Fact]
    public void Неизвестный_класс_linac_закупается_с_дефолтным_профилем()
    {
        Assert.False(Balance.ShipClasses.ContainsKey("linac")); // класса нет в спецификации (старые тесты его используют)

        var fleets = TestDomain.Factory.CreateFleetRegistry(Balance);
        var matchId = new MatchId("m1");
        var player = new PlayerId("a");
        var ships = RepeatUnknown(10); // 10 * Cost(10) = ровно 100

        fleets.SetFleet(matchId, player, ships);

        Assert.NotNull(fleets.TryGetFleet(matchId, player));
        Assert.Equal("linac", fleets.TryGetFleet(matchId, player)!.Ships[0].Class);
    }

    [Fact]
    public void Неизвестный_класс_не_обходит_капу()
    {
        var fleets = TestDomain.Factory.CreateFleetRegistry(Balance);
        var matchId = new MatchId("m1");
        var player = new PlayerId("a");
        var ships = RepeatUnknown(11); // 11 * Cost(10) = 110 > 100

        Assert.Throws<InvalidOperationException>(() => fleets.SetFleet(matchId, player, ships));
    }

    [Fact]
    public void Капа_берётся_из_конфига_а_не_из_кода()
    {
        var config = Balance with { PurchaseCapPoints = 20 }; // компактная капа для сценария
        var fleets = TestDomain.Factory.CreateFleetRegistry(config);
        var matchId = new MatchId("m1");
        var player = new PlayerId("a");

        fleets.SetFleet(matchId, player, Repeat("medium", 2)); // 16 < 20
        Assert.NotNull(fleets.TryGetFleet(matchId, player));

        Assert.Throws<InvalidOperationException>(() =>
            fleets.SetFleet(matchId, player, Repeat("medium", 3))); // 24 > 20
    }
}
