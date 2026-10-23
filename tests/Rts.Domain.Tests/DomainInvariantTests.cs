namespace Rts.Domain.Tests;

public class DomainInvariantTests
{
    [Fact]
    public void Баланс_отвергает_некорректные_значения()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BalanceConfig(baseDamagePerTick: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BalanceConfig(readyTimeout: TimeSpan.Zero));
    }

    [Fact]
    public void Корабль_не_получает_корпусной_урон_сверх_нуля()
    {
        var ship = new ShipUnit(new ShipId("s"), "scout", 10, 0);
        var damage = ship.ReceiveDamage(100, 0);
        Assert.Equal(10, damage);
        Assert.Equal(0, ship.Hull);
        Assert.True(ship.Destroyed);
    }

    [Fact]
    public void Рейтинг_не_разрешает_ничью_в_исходе()
    {
        Assert.Throws<ArgumentException>(() => new BattleOutcome(new PlayerId("a"), new PlayerId("a")));
    }
}
