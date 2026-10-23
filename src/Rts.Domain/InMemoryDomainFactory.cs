namespace Rts.Domain;

/// <summary>Фабрика домен-сервисов in-memory: сервисы зависят только от своего конфига и абстракций.</summary>
public class InMemoryDomainFactory : IDomainFactory
{
    public IMatchmakingService CreateLobby(BalanceConfig config) => new LobbyService(config);

    public IFleetRegistry CreateFleetRegistry() => new FleetRegistry();

    public ICombatService CreateCombat(BalanceConfig config) => new CombatService(config);

    public IRatingRepository CreateRating(BalanceConfig config) => new RatingRepository(config);
}