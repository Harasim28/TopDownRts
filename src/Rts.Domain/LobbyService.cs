namespace Rts.Domain;

public sealed class Lobby
{
    private Match _match;
    private TimeSpan _waiting;
    private bool _closed;
    public Match Match => _match;
    public bool IsClosed => _closed;

    public Lobby(Match match) { ArgumentNullException.ThrowIfNull(match); _match = match; }
    public Match Ready(PlayerId player)
    {
        if (_closed || (player != _match.A && player != _match.B)) throw new InvalidOperationException("Игрок не состоит в матче лобби");
        _match = _match with { ReadyA = _match.ReadyA || _match.A == player, ReadyB = _match.ReadyB || _match.B == player };
        _match = _match with { State = _match.ReadyA && _match.ReadyB ? ReadyState.BothReady : ReadyState.WaitingReady };
        return _match;
    }
    public bool Advance(TimeSpan elapsed, TimeSpan timeout)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (_closed || _match.State != ReadyState.WaitingReady) return false;
        _waiting += elapsed;
        if (_waiting < timeout) return false;
        _closed = true;
        _match = _match with { State = ReadyState.TimedOut };
        return true;
    }
    public void Decline() => _closed = true;
}

public sealed class LobbyService : IMatchmakingService
{
    private readonly BalanceConfig _config;
    private readonly List<PlayerId> _queue = new();
    private readonly List<Lobby> _lobbies = new();
    private int _nextMatchNumber;
    public LobbyService(BalanceConfig config) { ArgumentNullException.ThrowIfNull(config); _config = config; }

    public Match? Join(PlayerId player)
    {
        var existing = TryGetMatch(player);
        if (existing is not null) return existing;
        if (_queue.Contains(player)) return null;
        _queue.Add(player);
        if (_queue.Count < 2) return null;
        var a = _queue[0]; var b = _queue[1]; _queue.RemoveRange(0, 2);
        var lobby = new Lobby(new Match(new MatchId($"m{++_nextMatchNumber}"), a, b, ReadyState.WaitingReady, false, false));
        _lobbies.Add(lobby);
        return lobby.Match;
    }
    public Match? TryGetMatch(PlayerId player) => _lobbies.FirstOrDefault(l => !l.IsClosed && (l.Match.A == player || l.Match.B == player))?.Match;
    public Match Ready(PlayerId player) => _lobbies.FirstOrDefault(l => !l.IsClosed && (l.Match.A == player || l.Match.B == player))?.Ready(player) ?? throw new InvalidOperationException("Игрок не состоит в матче лобби");
    public void Decline(PlayerId player) { var lobby = _lobbies.FirstOrDefault(l => !l.IsClosed && (l.Match.A == player || l.Match.B == player)); if (lobby is not null) lobby.Decline(); }
    public IReadOnlyList<Match> Tick(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        var result = new List<Match>();
        foreach (var lobby in _lobbies.Where(l => !l.IsClosed && l.Advance(elapsed, _config.ReadyTimeout))) result.Add(lobby.Match);
        return result;
    }
}
