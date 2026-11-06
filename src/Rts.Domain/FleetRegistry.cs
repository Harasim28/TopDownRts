namespace Rts.Domain;

/// <summary>
/// Реестр флотов in-memory: состав можно менять до подтверждения.
/// Кадр 3b (P6): закупка ограничена капой PurchaseCapPoints НА КАЖДОГО ИГРОКА отдельно,
/// стоимость корабля — Cost профиля его класса из конфига (неизвестный класс — дефолтный профиль).
/// </summary>
public class FleetRegistry : IFleetRegistry
{
    private readonly BalanceConfig _config;
    private readonly Dictionary<(MatchId Match, PlayerId Player), Fleet> _fleets = new();
    private readonly HashSet<(MatchId Match, PlayerId Player)> _confirmed = new();

    public FleetRegistry() : this(new BalanceConfig()) { }

    public FleetRegistry(BalanceConfig config)
    {
        _config = config;
    }

    public void SetFleet(MatchId match, PlayerId player, IReadOnlyList<ShipUnit> ships)
    {
        var key = (match, player);
        var cost = ships.Sum(s => ProfileFor(s.Class).Cost);
        if (cost > _config.PurchaseCapPoints)
        {
            // отказ ДО всякой мутации: прежний состав и подтверждение не тронуты
            throw new InvalidOperationException(
                $"Состав флота стоит {cost} очков закупа; капа на игрока {_config.PurchaseCapPoints} превышена");
        }

        _fleets[key] = new Fleet(player, _fleets.TryGetValue(key, out var old) ? old.Team : new Team(0), ships);
        _confirmed.Remove(key); // новая правка состава требует повторного подтверждения
    }

    public void ConfirmFleet(MatchId match, PlayerId player)
    {
        var key = (match, player);
        if (!_fleets.ContainsKey(key))
        {
            throw new InvalidOperationException("Состав флота ещё не выставлен");
        }

        _confirmed.Add(key);
    }

    public bool IsConfirmed(MatchId match, PlayerId player) => _confirmed.Contains((match, player));

    public Fleet? TryGetFleet(MatchId match, PlayerId player) =>
        _fleets.TryGetValue((match, player), out var fleet) ? fleet : null;

    /// <summary>Профиль класса: известный — из спецификации конфига, неизвестный (старые составы) — дефолтный.</summary>
    private ShipClassProfile ProfileFor(string @class) =>
        _config.ShipClasses.TryGetValue(@class, out var profile) ? profile : _config.UnknownClassProfile;
}
