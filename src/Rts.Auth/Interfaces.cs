namespace Rts.Auth;

/// <summary>Решение аутентификации игрока.</summary>
public enum AuthDecision
{
    /// <summary>Допускаем в игру.</summary>
    Allowed,

    /// <summary>Нужен второй этап: код, отправленный на почту.</summary>
    PendingEmail,

    /// <summary>Код подтверждения неверный.</summary>
    Rejected,
}

/// <summary>Итог попытки аутентификации.</summary>
public record AuthResult(AuthDecision Decision)
{
    public static AuthResult Allowed() => new(AuthDecision.Allowed);
    public static AuthResult PendingEmail() => new(AuthDecision.PendingEmail);
    public static AuthResult Rejected() => new(AuthDecision.Rejected);
}

/// <summary>Хранилище токенов устройств игроков (деталь реализации, за интерфейсом — БД).</summary>
public interface IDeviceTokenStore
{
    /// <summary>Сохранённый токен устройства игрока или null, если его ещё нет.</summary>
    string? GetDeviceToken(string playerId);

    /// <summary>Запомнить токен устройства игрока.</summary>
    void SaveDeviceToken(string playerId, string deviceToken);
}

/// <summary>Сервис отправки писем с кодом подтверждения (деталь реализации).</summary>
public interface IEmailCodeSender
{
    void Send(string email, string code);
}

/// <summary>Генератор одноразовых кодов подтверждения.</summary>
public interface ICodeGenerator
{
    string NewCode();
}

/// <summary>Хранилище одноразовых кодов подтверждения.</summary>
public interface IConfirmationCodeStore
{
    /// <summary>Сохранить код для игрока.</summary>
    void Save(string playerId, string code);

    /// <summary>Проверить и погасить код (повторная проверка тем же кодом даёт false).</summary>
    bool VerifyAndConsume(string playerId, string code);
}

/// <summary>
/// Высокоуровневая политика входа игрока (зависит только от абстракций, DIP).
/// Правило: если предъявленный токен устройства совпадает с сохранённым — второй этап не нужен;
/// иначе код подтверждения отправляется на почту.
/// </summary>
public interface IPlayerAuthenticator
{
    /// <summary>Первый этап: пароль уже сверен, проверяем токен устройства.</summary>
    AuthResult TryLogin(string playerId, string email, string deviceToken);

    /// <summary>Второй этап: игрок ввёл код из письма. При успехе запоминает токен устройства.</summary>
    AuthResult Confirm(string playerId, string code, string deviceToken);
}