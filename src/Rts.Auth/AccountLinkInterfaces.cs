namespace Rts.Auth;

// ---- Связка кадра 0 с 2FA: «успешный вход → профиль по почте» (аддитивно, кадр 0) ----

/// <summary>
/// Поиск профиля по почте — аддитивная способность реестра для связки «вход → профиль».
/// Существующий IPlayerRegistry не расширяется: новая способность объявлена здесь отдельным
/// интерфейсом (та же реализация — PlayerRegistry), старые вызовы и тесты не менялись.
/// </summary>
public interface IProfileByEmail
{
    /// <summary>Профиль с такой почтой или null, если игрок не регистрировался.</summary>
    PlayerProfile? TryGetByEmail(string email);
}

/// <summary>Успешная связка: вход разрешён 2FA, профиль найден в реестре по почте.</summary>
public sealed record AccountLinked(PlayerProfile Profile);

/// <summary>
/// Обёртка над IPlayerAuthenticator (связка кадра 0 с 2FA): успех входа (Allowed) тут же
/// сопоставляется с профилем реестра по почте. Реестру адресов на регистацию нет — связка
/// ТОЛЬКО читает профили; нет профиля с такой почтой — отказ, дубли профилей она не заводит.
/// </summary>
public interface IAccountLinkService
{
    /// <summary>
    /// Обёртка TryLogin: токен совпал (Allowed) → профиль по почте (нет — InvalidOperationException);
    /// код ушёл на почту (PendingEmail) → null, почта запоминается до второго этапа.
    /// </summary>
    AccountLinked? LoginAndLink(string playerId, string email, string deviceToken);

    /// <summary>
    /// Обёртка Confirm: код верный → связка по запомненной на первом этапе почте (связали — забыли);
    /// вход напрямик, без запомненной почты → InvalidOperationException с объяснением
    /// (2FA почту не хранит, связывать нечем); код неверный → null (вход не разрешён, связки нет).
    /// </summary>
    AccountLinked? ConfirmAndLink(string playerId, string code, string deviceToken);
}
