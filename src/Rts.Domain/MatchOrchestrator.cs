namespace Rts.Domain;

/// <summary>
/// Слой сборки матча в бой in-memory. Лобби, флоты и бой остаются независимыми сервисами;
/// оркестратор следит за их состояниями: прикреплённый матч (игроки A и B), BothReady в лобби,
/// подтверждение обоих флотов. В момент, когда оба условия выполнены, стартует бой
/// через ICombatService.Start и появляется MatchStarted — ровно один раз.
/// </summary>
public class MatchOrchestrator : IMatchOrchestrator
{
    private readonly IMatchmakingService _lobby;
    private readonly IFleetRegistry _fleets;
    private readonly ICombatService _combat;
    private readonly Dictionary<MatchId, (PlayerId A, PlayerId B)> _pairs = new();
    private readonly Dictionary<MatchId, Battle> _battles = new();

    public MatchOrchestrator(IMatchmakingService lobby, IFleetRegistry fleets, ICombatService combat)
    {
        _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
        _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
    }

    public void Attach(Match match) =>
        _pairs[RequireNotNull(match).Id] = (match.A, match.B);

    public void SetFleet(MatchId match, PlayerId player, IReadOnlyList<ShipUnit> ships) =>
        _fleets.SetFleet(match, player, ships);

    public MatchStarted? ConfirmFleet(MatchId match, PlayerId player)
    {
        RequireAttached(match);
        _fleets.ConfirmFleet(match, player);
        return TryStartBattle(match); // этим подтверждением мог закрыться последний флот
    }

    public MatchStarted? TryStartBattle(MatchId match)
    {
        RequireAttached(match);

        if (_battles.ContainsKey(match))
        {
            return null; // бой стартует ровно один раз
        }

        var (a, b) = _pairs[match];
        if (!BothReady(match) || !_fleets.IsConfirmed(match, a) || !_fleets.IsConfirmed(match, b))
        {
            return null; // условие AllFleetsConfirmed (+BothReady) ещё не выполнено
        }

        var fleetA = _fleets.TryGetFleet(match, a)
            ?? throw new InvalidOperationException("Флот игрока A не выставлен");
        var fleetB = _fleets.TryGetFleet(match, b)
            ?? throw new InvalidOperationException("Флот игрока B не выставлен");

        var battle = _combat.Start(match, fleetA, fleetB);
        _battles[match] = battle;
        return new MatchStarted(match, a, b, battle);
    }

    public bool AllFleetsConfirmed(MatchId match)
    {
        var (a, b) = Pair(match);
        return _fleets.IsConfirmed(match, a) && _fleets.IsConfirmed(match, b);
    }

    public Battle? TryGetBattle(MatchId match) =>
        _battles.TryGetValue(match, out var battle) ? battle : null;

    private static Match RequireNotNull(Match match) =>
        match ?? throw new ArgumentNullException(nameof(match));

    private (PlayerId A, PlayerId B) Pair(MatchId match) =>
        _pairs.TryGetValue(match, out var pair)
            ? pair
            : throw new InvalidOperationException("Матч не прикреплён к оркестратору");

    private void RequireAttached(MatchId match) => _ = Pair(match);

    /// <summary>Лобби-матч сейчас в BothReady? Состояние читаем у лобби — оно живое, не снапшот.</summary>
    private bool BothReady(MatchId match)
    {
        var (a, _) = _pairs[match];
        var live = _lobby.TryGetMatch(a);
        return live is not null && live.Id == match && live.State == ReadyState.BothReady;
    }
}
