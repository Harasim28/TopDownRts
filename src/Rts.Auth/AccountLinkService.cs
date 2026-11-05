namespace Rts.Auth;

/// <summary>
/// Связка «вход → профиль» (кадр 0): оборачивает политику 2FA и реестр. Её состояние —
/// ТОЛЬКО запомненная почта первого этапа (на каждого игрока): второй этап Confirm сам почту
/// не передаёт. Повторная связка того же входа идемпотентна — возвращает тот же профиль и
/// ничего не заводит; вход напрямик (Confirm без запомненной почты) — отказ с объяснением.
/// </summary>
public class AccountLinkService : IAccountLinkService
{
    private readonly IPlayerAuthenticator _authenticator;
    private readonly IProfileByEmail _profilesByEmail;
    private readonly Dictionary<string, string> _rememberedEmails = new(StringComparer.Ordinal);

    public AccountLinkService(IPlayerAuthenticator authenticator, IProfileByEmail profilesByEmail)
    {
        _authenticator = authenticator;
        _profilesByEmail = profilesByEmail;
    }

    /// <summary>Обёртка TryLogin: Allowed → связка по почте; PendingEmail → null (код на почту).</summary>
    public AccountLinked? LoginAndLink(string playerId, string email, string deviceToken)
    {
        var decision = _authenticator.TryLogin(playerId, email, deviceToken);
        switch (decision.Decision)
        {
            case AuthDecision.Allowed:
                return BuildLink(playerId, RememberEmail(playerId, email));

            case AuthDecision.PendingEmail:
                // код ушёл на почту; связка будет после Confirm — почту запоминаем ТОЛЬКО здесь:
                // Confirm сам её не передаёт. Запоминаем даже без профиля: профиль могут
                // зарегистрировать к моменту второго этапа.
                RememberEmail(playerId, email);
                return null;

            default:
                return null; // вход не разрешён — связки нет
        }
    }

    /// <summary>Обёртка Confirm: Allowed → связка по запомненной почте; напрямик — отказ.</summary>
    public AccountLinked? ConfirmAndLink(string playerId, string code, string deviceToken)
    {
        var decision = _authenticator.Confirm(playerId, code, deviceToken);
        if (decision.Decision != AuthDecision.Allowed)
        {
            return null; // код неверный/использованный — вход не разрешён, связки нет
        }

        if (!_rememberedEmails.TryGetValue(playerId, out var email))
        {
            throw new InvalidOperationException(
                $"Вход напрямик: для игрока '{playerId}' запомненной почты нет"
                + " (2FA её не хранит) — свяжите вход через LoginAndLink, там почта известна");
        }

        var linked = BuildLink(playerId, email);
        _rememberedEmails.Remove(playerId); // связали — память о почте больше не нужна
        return linked;
    }

    private string RememberEmail(string playerId, string email)
    {
        _rememberedEmails[playerId] = email;
        return email;
    }

    private AccountLinked BuildLink(string playerId, string email)
    {
        var profile = _profilesByEmail.TryGetByEmail(email)
            ?? throw new InvalidOperationException(
                $"Вход игрока '{playerId}' разрешён, но профиля с почтой '{email}' нет —"
                + " связать не с чем (сначала RegisterPlayer)");

        return new AccountLinked(profile);
    }
}
