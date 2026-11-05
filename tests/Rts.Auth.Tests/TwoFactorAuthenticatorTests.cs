using Xunit;

namespace Rts.Auth.Tests;

/// <summary>Тестовые подставки (детали реализации): память вместо БД, запись вместо почты.</summary>
internal class InMemoryDeviceTokenStore : IDeviceTokenStore
{
    private readonly Dictionary<string, string> _tokens = new();
    public string? GetDeviceToken(string playerId) => _tokens.TryGetValue(playerId, out var t) ? t : null;
    public void SaveDeviceToken(string playerId, string deviceToken) => _tokens[playerId] = deviceToken;
}

internal class InMemoryConfirmationCodeStore : IConfirmationCodeStore
{
    private readonly Dictionary<string, string> _codes = new();
    public void Save(string playerId, string code) => _codes[playerId] = code;
    public bool VerifyAndConsume(string playerId, string code) =>
        _codes.TryGetValue(playerId, out var saved) && saved == code && _codes.Remove(playerId);
}

internal class RecordingEmailSender : IEmailCodeSender
{
    public List<(string Email, string Code)> Sent { get; } = new();
    public void Send(string email, string code) => Sent.Add((email, code));
}

internal class FixedCodeGenerator : ICodeGenerator
{
    public string Next { get; set; } = "135790";
    public string NewCode() => Next;
}

/// <summary>Собранная система: политика + подставки.</summary>
internal sealed class AuthSut
{
    public InMemoryDeviceTokenStore Tokens { get; } = new();
    public InMemoryConfirmationCodeStore Codes { get; } = new();
    public RecordingEmailSender Email { get; } = new();
    public FixedCodeGenerator Generator { get; } = new();
    public IPlayerAuthenticator Authenticator { get; }

    public AuthSut()
        => Authenticator = new TwoFactorAuthenticator(Tokens, Email, Codes, Generator);
}

public class TwoFactorAuthenticatorTests
{
    [Fact]
    public void Токен_совпал_пропускаем_второй_этап()
    {
        var sut = new AuthSut();
        sut.Tokens.SaveDeviceToken("p1", "device-a");

        var result = sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-a");

        Assert.Equal(AuthDecision.Allowed, result.Decision);
        Assert.Empty(sut.Email.Sent); // письмо не отправляется
    }

    [Fact]
    public void Токен_не_совпал_отправляем_код_на_почту()
    {
        var sut = new AuthSut();
        sut.Tokens.SaveDeviceToken("p1", "device-old");

        var result = sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-new");

        Assert.Equal(AuthDecision.PendingEmail, result.Decision);
        var letter = Assert.Single(sut.Email.Sent);
        Assert.Equal("player@mail.ru", letter.Email);
        Assert.Equal("135790", letter.Code); // код совпадает с тем, что сохранён
        Assert.Equal(sut.Codes, sut.Codes); // магазин кодов — тот же объект
    }

    [Fact]
    public void Первый_вход_без_сохранённого_токена_тоже_через_почту()
    {
        var sut = new AuthSut();

        var result = sut.Authenticator.TryLogin("p-new", "new@mail.ru", "device-x");

        Assert.Equal(AuthDecision.PendingEmail, result.Decision);
        Assert.Single(sut.Email.Sent);
    }

    [Fact]
    public void Код_из_письма_верный_допуск_и_токен_запоминается()
    {
        var sut = new AuthSut();
        sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-new");

        var result = sut.Authenticator.Confirm("p1", "135790", "device-new");

        Assert.Equal(AuthDecision.Allowed, result.Decision);
        var sentCount = sut.Email.Sent.Count; // письмо первого этапа уже ушло
        var now = sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-new");
        Assert.Equal(AuthDecision.Allowed, now.Decision);
        Assert.Equal(sentCount, sut.Email.Sent.Count); // повторный вход — БЕЗ нового письма
    }

    [Fact]
    public void Код_неверный_отказ()
    {
        var sut = new AuthSut();
        sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-new");

        var result = sut.Authenticator.Confirm("p1", "999999", "device-new");

        Assert.Equal(AuthDecision.Rejected, result.Decision);
    }

    [Fact]
    public void Код_одноразовый_повторное_подтверждение_отказ()
    {
        var sut = new AuthSut();
        sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-new");

        sut.Authenticator.Confirm("p1", "135790", "device-new");
        var again = sut.Authenticator.Confirm("p1", "135790", "device-new");

        Assert.Equal(AuthDecision.Rejected, again.Decision);
    }

    [Fact]
    public void Отправленный_в_другой_раз_код_не_принимается()
    {
        var sut = new AuthSut();
        sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-new");
        sut.Generator.Next = "246800";
        sut.Authenticator.TryLogin("p1", "player@mail.ru", "device-new"); // перегенерация кода переписывает сохранённый

        var result = sut.Authenticator.Confirm("p1", "135790", "device-new");

        Assert.Equal(AuthDecision.Rejected, result.Decision);
    }
}