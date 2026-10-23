namespace Rts.Domain;

public enum ReadyState { WaitingReady, BothReady, TimedOut }

public sealed record Match(MatchId Id, PlayerId A, PlayerId B, ReadyState State, bool ReadyA, bool ReadyB);

public interface IMatchmakingService
{
    Match? Join(PlayerId player);
    Match? TryGetMatch(PlayerId player);
    Match Ready(PlayerId player);
    void Decline(PlayerId player);
    IReadOnlyList<Match> Tick(TimeSpan elapsed);
}
