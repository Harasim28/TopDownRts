namespace Rts.Domain;

public readonly record struct PlayerId(string Value);
public readonly record struct MatchId(string Value);
public readonly record struct ShipId(string Value);
public readonly record struct Team(byte Index);
