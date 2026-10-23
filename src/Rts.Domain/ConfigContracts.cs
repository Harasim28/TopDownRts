namespace Rts.Domain;

/// <summary>Неизменяемый баланс домена: все значения задаются и проверяются конструктором.</summary>
public sealed record BalanceConfig
{
    public TimeSpan ReadyTimeout { get; }
    public int BaseDamagePerTick { get; }
    public int ShieldAbsorbsPerTick { get; }
    public int RatingWinPoints { get; }
    public int RatingLosePoints { get; }

    public BalanceConfig(
        TimeSpan? readyTimeout = null,
        int baseDamagePerTick = 10,
        int shieldAbsorptionPerTick = 6,
        int ratingWinPoints = 15,
        int ratingLosePoints = 10)
    {
        var timeout = readyTimeout ?? TimeSpan.FromSeconds(30);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(readyTimeout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseDamagePerTick);
        ArgumentOutOfRangeException.ThrowIfNegative(shieldAbsorptionPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratingWinPoints);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratingLosePoints);
        ReadyTimeout = timeout;
        BaseDamagePerTick = baseDamagePerTick;
        ShieldAbsorbsPerTick = shieldAbsorptionPerTick;
        RatingWinPoints = ratingWinPoints;
        RatingLosePoints = ratingLosePoints;
    }

    public static BalanceConfig Default => new();
}

public interface IDomainFactory
{
    IMatchmakingService CreateLobby(BalanceConfig config);
    IFleetRegistry CreateFleetRegistry();
    ICombatService CreateCombat(BalanceConfig config);
    IRatingRepository CreateRating(BalanceConfig config);
}
