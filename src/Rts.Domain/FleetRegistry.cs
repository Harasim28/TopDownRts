namespace Rts.Domain;

/// <summary>Реестр флотов in-memory: состав можно менять до подтверждения.</summary>
public class FleetRegistry : IFleetRegistry
{
    private readonly Dictionary<(MatchId Match, PlayerId Player), Fleet> _fleets = new();
    private readonly HashSet<(MatchId Match, PlayerId Player)> _confirmed = new();

    public void SetFleet(MatchId match, PlayerId player, IReadOnlyList<ShipUnit> ships)
    {
        var key = (match, player);
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
}