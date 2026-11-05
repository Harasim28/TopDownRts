namespace Rts.Auth;

/// <summary>
/// Политика двухэтапной аутентификации игрока.
/// Токен устройства совпадает с сохранённым — пускаем сразу;
/// иначе генерируем одноразовый код, отправляем его на почту и ждём подтверждения.
/// Зависит только от абстракций (DIP): хранилища и отправитель приходят извне.
/// </summary>
public class TwoFactorAuthenticator : IPlayerAuthenticator
{
    private readonly IDeviceTokenStore _tokens;
    private readonly IEmailCodeSender _email;
    private readonly IConfirmationCodeStore _codes;
    private readonly ICodeGenerator _codeGenerator;

    public TwoFactorAuthenticator(
        IDeviceTokenStore tokens,
        IEmailCodeSender email,
        IConfirmationCodeStore codes,
        ICodeGenerator codeGenerator)
    {
        _tokens = tokens;
        _email = email;
        _codes = codes;
        _codeGenerator = codeGenerator;
    }

    /// <summary>Первый этап: пароль уже сверен, сравниваем токен устройства.</summary>
    public AuthResult TryLogin(string playerId, string email, string deviceToken)
    {
        var saved = _tokens.GetDeviceToken(playerId);
        if (saved is not null && saved == deviceToken)
        {
            return AuthResult.Allowed();
        }

        var code = _codeGenerator.NewCode();
        _codes.Save(playerId, code);
        _email.Send(email, code);
        return AuthResult.PendingEmail();
    }

    /// <summary>Второй этап: игрок ввёл код из письма; при успехе запоминаем его устройство.</summary>
    public AuthResult Confirm(string playerId, string code, string deviceToken)
    {
        if (!_codes.VerifyAndConsume(playerId, code))
        {
            return AuthResult.Rejected();
        }

        _tokens.SaveDeviceToken(playerId, deviceToken);
        return AuthResult.Allowed();
    }
}