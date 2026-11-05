using Xunit;

namespace Rts.Auth.Tests;

/// <summary>
/// Сценарии связки «вход → профиль» (кадр 0): успешный 2FA-логин (Allowed) сопоставляется
/// с профилем из реестра ПО ПОЧТЕ; вход без почты (напрямик Confirm) с профилем не связывается.
/// Тесты знают только контракты связки и реестра — 2FA подключается как есть, без правок.
/// </summary>
public class AccountLinkTests
{
    /// <summary>Собранная система: 2FA-политика + реестр профилей + связка (как в AuthSut, плюс реестр).</summary>
    internal sealed class LinkSut
    {
        public InMemoryDeviceTokenStore Tokens { get; } = new();
        public InMemoryConfirmationCodeStore Codes { get; } = new();
        public RecordingEmailSender Email { get; } = new();
        public FixedCodeGenerator Generator { get; } = new();
        public PlayerRegistry Registry { get; } = new();
        public IPlayerAuthenticator Authenticator { get; }
        public IAccountLinkService Link { get; }

        public LinkSut(bool profileRegistered = true)
        {
            Authenticator = new TwoFactorAuthenticator(Tokens, Email, Codes, Generator);
            Link = new AccountLinkService(Authenticator, Registry);
            if (profileRegistered)
            {
                Registry.RegisterPlayer("p1", "player@mail.ru");
            }
        }
    }

    [Fact]
    public void Вход_разрешён_связывается_с_профилем_по_совпавшей_почте()
    {
        var sut = new LinkSut();
        sut.Tokens.SaveDeviceToken("p1", "device-a");

        var linked = sut.Link.LoginAndLink("p1", "player@mail.ru", "device-a");

        Assert.NotNull(linked);
        Assert.Same(sut.Registry.TryGet("p1"), linked!.Profile); // именно профиль из реестра
        Assert.Equal("p1", linked.Profile.PlayerId);
        Assert.Equal("player@mail.ru", linked.Profile.Email);
    }

    [Fact]
    public void Вход_успешен_а_профиля_с_такой_почтой_нет_отказ_связки()
    {
        var sut = new LinkSut(profileRegistered: false);
        sut.Tokens.SaveDeviceToken("ghost", "device-a");

        Assert.Throws<InvalidOperationException>(
            () => sut.Link.LoginAndLink("ghost", "nobody@mail.ru", "device-a"));
    }

    [Fact]
    public void Вход_на_этапе_кода_связку_не_заводит()
    {
        var sut = new LinkSut();

        var linked = sut.Link.LoginAndLink("p1", "player@mail.ru", "device-new");

        Assert.Null(linked); // код ещё на почте — связка будет только после подтверждения
        var letter = Assert.Single(sut.Email.Sent);
        Assert.Equal("player@mail.ru", letter.Email);
    }

    [Fact]
    public void Подтверждение_кода_связывает_по_запомненной_на_входе_почте()
    {
        var sut = new LinkSut();
        sut.Link.LoginAndLink("p1", "player@mail.ru", "device-new"); // PendingEmail, почта запомнена

        var linked = sut.Link.ConfirmAndLink("p1", "135790", "device-new");

        Assert.NotNull(linked);
        Assert.Same(sut.Registry.TryGet("p1"), linked!.Profile);
    }

    [Fact]
    public void Вход_напрямик_без_запомненной_почты_отказ_с_объяснением()
    {
        var sut = new LinkSut();
        sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-new"); // код ушёл МИМО связки

        Assert.Throws<InvalidOperationException>(
            () => sut.Link.ConfirmAndLink("p1", "135790", "device-new"));
    }

    [Fact]
    public void Повторная_связка_того_же_входа_тот_же_профиль_без_дублей()
    {
        var sut = new LinkSut();
       sut.Tokens.SaveDeviceToken("p1", "device-a");

        var first = sut.Link.LoginAndLink("p1", "player@mail.ru", "device-a");
        var second = sut.Link.LoginAndLink("p1", "player@mail.ru", "device-a");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first!.Profile, second!.Profile); // тот же профиль, дублей связка не заводит
        Assert.Throws<InvalidOperationException>( // реестр не тронут: тот же id занят
            () => sut.Registry.RegisterPlayer("p1", "player@mail.ru"));
    }
}
