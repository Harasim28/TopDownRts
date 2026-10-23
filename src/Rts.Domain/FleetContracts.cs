namespace Rts.Domain;

public sealed class ShipUnit
{
    public ShipId Id { get; }
    public string Class { get; }
    public int MaxHull { get; }
    public int MaxShield { get; }
    public int Hull { get; private set; }
    public int Shield { get; private set; }
    public bool Destroyed => Hull == 0;

    public ShipUnit(ShipId id, string @class, int maxHull, int maxShield)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@class);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHull);
        ArgumentOutOfRangeException.ThrowIfNegative(maxShield);
        Id = id; Class = @class; MaxHull = maxHull; MaxShield = maxShield;
        Hull = maxHull; Shield = maxShield;
    }

    public int ReceiveDamage(int damage, int shieldAbsorption)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(damage);
        ArgumentOutOfRangeException.ThrowIfNegative(shieldAbsorption);
        if (Destroyed) return 0;
        var absorbed = Math.Min(Math.Min(shieldAbsorption, Shield), damage);
        Shield -= absorbed;
        var hullDamage = Math.Min(damage - absorbed, Hull);
        Hull = Math.Max(0, Hull - hullDamage);
        return hullDamage;
    }
}

public sealed class Fleet
{
    private readonly List<ShipUnit> _ships;
    public PlayerId Owner { get; }
    public Team Team { get; }
    public IReadOnlyList<ShipUnit> Ships => _ships;
    public bool IsWiped => _ships.All(s => s.Destroyed);

    public Fleet(PlayerId owner, Team team, IEnumerable<ShipUnit> ships)
    {
        ArgumentNullException.ThrowIfNull(ships);
        _ships = ships.ToList();
        if (_ships.Count == 0) throw new ArgumentException("Флот должен содержать хотя бы один корабль", nameof(ships));
        Owner = owner; Team = team;
    }

    public int FireAt(Fleet defender, int damagePerShip, int shieldAbsorption)
    {
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(damagePerShip);
        ArgumentOutOfRangeException.ThrowIfNegative(shieldAbsorption);
        if (IsWiped || defender.IsWiped) return 0;
        var target = defender._ships.First(s => !s.Destroyed);
        var volley = _ships.Count(s => !s.Destroyed) * damagePerShip;
        return target.ReceiveDamage(volley, shieldAbsorption);
    }
}

public interface IFleetRegistry
{
    void SetFleet(MatchId match, PlayerId player, IReadOnlyList<ShipUnit> ships);
    void ConfirmFleet(MatchId match, PlayerId player);
    bool IsConfirmed(MatchId match, PlayerId player);
    Fleet? TryGetFleet(MatchId match, PlayerId player);
}
