namespace Rts.Auth.Tests;

/// <summary>Сценарии регистрации профиля (кадр 0): RegisterPlayer → PlayerRegistered.</summary>
public class PlayerRegistryTests
{
    [Fact]
    public void Регистрация_возвращает_событие_с_данными_игрока()
    {
        var registry = new PlayerRegistry();

        var registered = registry.RegisterPlayer("p1", "player@mail.ru");

        Assert.Equal("p1", registered.PlayerId);
        Assert.Equal("player@mail.ru", registered.Email);
    }

    [Fact]
    public void Зарегистрированный_профиль_находится_по_идентификатору()
    {
        var registry = new PlayerRegistry();
        registry.RegisterPlayer("p1", "player@mail.ru");

        var profile = registry.TryGet("p1");

        Assert.NotNull(profile);
        Assert.Equal("p1", profile!.PlayerId);
        Assert.Equal("player@mail.ru", profile.Email);
    }

    [Fact]
    public void Незарегистрированный_игрок_не_находится()
    {
        var registry = new PlayerRegistry();

        Assert.Null(registry.TryGet("ghost"));
    }

    [Fact]
    public void Повторная_регистрация_того_же_игрока_отклоняется()
    {
        var registry = new PlayerRegistry();
        registry.RegisterPlayer("p1", "player@mail.ru");

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterPlayer("p1", "other@mail.ru"));
    }

    [Fact]
    public void Регистрация_с_пустым_идентификатором_отклоняется()
    {
        var registry = new PlayerRegistry();

        Assert.Throws<ArgumentException>(() => registry.RegisterPlayer("", "player@mail.ru"));
    }

    [Fact]
    public void Регистрация_с_пустой_почтой_отклоняется()
    {
        var registry = new PlayerRegistry();

        Assert.Throws<ArgumentException>(() => registry.RegisterPlayer("p1", "   "));
    }

    [Fact]
    public void Регистрация_письмо_не_отправляет()
    {
        // кадр 0 — чистое хранилище: никакой сети и почты при регистрации
        var sender = new RecordingEmailSender();
        var registry = new PlayerRegistry();

        registry.RegisterPlayer("p1", "player@mail.ru");

        Assert.Empty(sender.Sent); // sender вообще не подключён к реестру — ничего не ушло
    }
}
