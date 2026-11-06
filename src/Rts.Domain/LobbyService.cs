namespace Rts.Domain;

/// <summary>
/// Очередь лобби in-memory. Двое в очереди → матч; Ready от обоих → BothReady;
/// ждущий Ready свыше таймаута → ReadyTimeout, лобби сбрасывается (правило v4).
/// </summary>
public class LobbyService : IMatchmakingService
{
    private readonly BalanceConfig _config;
    private readonly List<PlayerId> _queue = new();
    private readonly List<LobbyEntry> _entries = new();
    private int _nextMatchNumber;

    private sealed class LobbyEntry
    {
        public Match Match { get; set; } = null!;
        public TimeSpan WaitingElapsed { get; set; }
        public bool Closed { get; set; }
    }

    public LobbyService(BalanceConfig config) => _config = config;

    public Match? Join(PlayerId player)
    {
        var existing = TryGetMatch(player);
        if (existing is not null)
        {
            return existing;
        }

        _queue.Add(player);
        if (_queue.Count < 2)
        {
            return null;
        }

        var a = _queue[0];
        var b = _queue[1];
        _queue.RemoveRange(0, 2);
        _nextMatchNumber++;
        var entry = new LobbyEntry
        {
            Match = new Match(
                new MatchId($"m{_nextMatchNumber}"),
                a,
                b,
                ReadyState.WaitingReady,
                ReadyA: false,
                ReadyB: false),
        };
        _entries.Add(entry);
        return entry.Match;
    }

    public Match? TryGetMatch(PlayerId player) =>
        _entries.FirstOrDefault(e =>
                !e.Closed && (e.Match.A == player || e.Match.B == player))?
            .Match;

    public Match Ready(PlayerId player)
    {
        var entry = _entries.FirstOrDefault(e =>
                !e.Closed && (e.Match.A == player || e.Match.B == player))
            ?? throw new InvalidOperationException("Игрок не состоит в матче лобби");

        var readyA = entry.Match.ReadyA || entry.Match.A == player;
        var readyB = entry.Match.ReadyB || entry.Match.B == player;
        var state = readyA && readyB ? ReadyState.BothReady : ReadyState.WaitingReady;
        entry.Match = entry.Match with { ReadyA = readyA, ReadyB = readyB, State = state };
        return entry.Match;
    }

    public void Decline(PlayerId player)
    {
        var entry = _entries.FirstOrDefault(e =>
            !e.Closed && (e.Match.A == player || e.Match.B == player));
        if (entry is null)
        {
            return;
        }

        entry.Closed = true;
        _queue.Remove(entry.Match.A);
        _queue.Remove(entry.Match.B);
    }

    public IReadOnlyList<Match> Tick(TimeSpan elapsed)
    {
        var timedOut = new List<Match>();

        foreach (var entry in _entries.Where(e => e.Closed is false))
        {
            if (entry.Match.State != ReadyState.WaitingReady)
            {
                continue;
            }

            entry.WaitingElapsed += elapsed;
            if (entry.WaitingElapsed >= _config.ReadyTimeout)
            {
                entry.Closed = true;
                var timedOutMatch = entry.Match with { State = ReadyState.TimedOut };
                timedOut.Add(timedOutMatch);
                _queue.Remove(timedOutMatch.A);
                _queue.Remove(timedOutMatch.B);
            }
        }

        return timedOut;
    }
}