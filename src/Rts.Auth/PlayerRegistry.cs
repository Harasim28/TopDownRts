namespace Rts.Auth;

/// <summary>
/// Реестр игроков in-memory: RegisterPlayer сохраняет профиль и выдаёт PlayerRegistered.
/// Никакой сети/БД/писем — регистрация только фиксирует профиль; за тем же интерфейсом
/// завтра может стоять хранилище.
/// </summary>
public class PlayerRegistry : IPlayerRegistry, IProfileByEmail
{
    private readonly Dictionary<string, PlayerProfile> _profiles = new(StringComparer.Ordinal);

    public PlayerRegistered RegisterPlayer(string playerId, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        if (_profiles.ContainsKey(playerId))
        {
            throw new InvalidOperationException($"Игрок '{playerId}' уже зарегистрирован");
        }

        var profile = new PlayerProfile(playerId, email);
        _profiles[playerId] = profile;
        return new PlayerRegistered(profile.PlayerId, profile.Email);
    }

    public PlayerProfile? TryGet(string playerId) =>
        _profiles.TryGetValue(playerId, out var profile) ? profile : null;

    /// <summary>
    /// Профиль по почте (аддитивно для связки «вход → профиль», IProfileByEmail):
    /// чистый поиск без побочных эффектов, null = профилей с такой почтой нет.
    /// </summary>
    public PlayerProfile? TryGetByEmail(string email) =>
        _profiles.Values.FirstOrDefault(profile => profile.Email == email);
}
