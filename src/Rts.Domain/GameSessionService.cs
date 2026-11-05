namespace Rts.Domain;

/// <summary>
/// Реализация фасада полного хода 2v2 (базовая рабочая версия, все решения — дефолтные,
/// см. README §5): участники комнаты в порядке очередности делятся пополам (2 A + 2 B);
/// лобби-матчи собираются крест-накрест по порядку A(0)↔B(0), A(1)↔B(1); оба матча прикреплены
/// к существующему<IMatchOrchestrator>, все четверо Ready; SetFleet/ConfirmFleet делегируются
/// оркестратору (капа закупа — внутри IFleetRegistry). MatchStarted лэйна = сессия стартует его
/// бой: корабли спавнятся у HomeA (команда A) и HomeB (команда B) с дефолтной рассадкой.
/// Тик: движение всех живых → присутствие в зонах всех живых → такт BaseControlService →
/// такт боя каждого лэйна, когда живые корабли его сторон сблизились ближе WeaponRange
/// (базы не воюют) → проверка исхода. Исходы: сторона уничтожена — победа другой; обе стороны
/// уничтожены в один тик или все лэйны доиграны при живых сторонах (счёт лэйнов 1:1 —
/// «одинаковое уничтожение») — ничья; победа по базам (таймер всех баз/порог очков). После
/// решённого исхода — рейтинг (по записи на пару победитель↔проигравший) и замерзание.
/// </summary>
public sealed class GameSessionService : IGameSession
{
    private sealed class Lane
    {
        public required Match Match;
        public Battle? Battle; // бой лэйна; ненулевой — лэйн стартовал (MatchReceived)
        public PlayerId PlayerA => Match.A;
        public PlayerId PlayerB => Match.B;
    }

    private static readonly Team TeamA = new(0);
    private static readonly Team TeamB = new(1);

    private readonly IMatchOrchestrator _orchestrator;
    private readonly ICombatService _combat;
    private readonly IBaseControlService _bases;
    private readonly IFleetMovementService _movement;
    private readonly IBaseMap _map;
    private readonly GameSessionTuning _tuning;
    private readonly IPolicyRatingRepository? _policyRatings; // подключённая политика рейтинга; null — плоская схема
    private readonly List<Lane> _lanes = new();
    private readonly Dictionary<MoveKey, (ShipUnit Ship, PlayerId Player)> _spawned = new();
    private readonly Dictionary<PlayerId, bool> _isTeamAByPlayer = new();
    private List<PlayerId> _teamAPlayers = new();
    private List<PlayerId> _teamBPlayers = new();
    private SessionOutcome _outcome = SessionOutcome.None;
    private Team? _winner;

    public GameSessionService(
        RoomId room,
        IReadOnlyList<PlayerId> participants,
        HostRuleSet rules,
        BalanceConfig? balance = null,
        MovementConfig? movement = null,
        GameSessionTuning? tuning = null,
        IDomainFactory? factory = null,
        IRatingPolicy? ratingPolicy = null)
    {
        Room = room;
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _tuning = tuning ?? GameSessionTuning.Default();
        balance ??= new BalanceConfig();
        factory ??= new InMemoryDomainFactory();
        _policyRatings = ratingPolicy is null
            ? null // дефолт: плоская схема «как есть» (±points из IRatingRepository)
            : new EloRatingRepository(ratingPolicy);

        // дефолт-сплит по порядку: первые двое — команда A, вторые двое — команда B (не подтверждено)
        if (participants is null || participants.Count != 4)
        {
            throw new InvalidOperationException("Полный ход 2v2 требует ровно четырёх участников комнаты");
        }

        _teamAPlayers = new List<PlayerId> { participants[0], participants[1] };
        _teamBPlayers = new List<PlayerId> { participants[2], participants[3] };
        foreach (var player in _teamAPlayers)
        {
            _isTeamAByPlayer[player] = true;
        }

        foreach (var player in _teamBPlayers)
        {
            _isTeamAByPlayer[player] = false;
        }

        Lobby = factory.CreateLobby(balance);
        Ratings = _policyRatings ?? factory.CreateRating(balance); // политика? хранилище политики : плоская схема
        _combat = factory.CreateCombat(balance);
        var fleets = factory.CreateFleetRegistry(balance);
        _orchestrator = factory.CreateOrchestrator(Lobby, fleets, _combat);
        _bases = new BaseControlService(Rules);

        var movementConfig = movement ?? MovementConfig.Default();
        _movement = new FleetMovementService(movementConfig);
        _map = new BattleMap(movementConfig);

        // дефолт-спаривание крест-накрест: очередь лобби соберёт пары A(0)↔B(0), A(1)↔B(1)
        _lanes.Add(BuildLane(TeamAPlayers[0], TeamBPlayers[0]));
        _lanes.Add(BuildLane(TeamAPlayers[1], TeamBPlayers[1]));
    }

    /// <summary>Открыть сессию полного хода по комнате: участники в порядке очередности → 2 A + 2 B.
    /// ratingPolicy (по умолчанию null — прежняя плоская схема) выбирает схему рейтинга после исхода.</summary>
    public static GameSessionService StartFromRoom(
        IRoomService rooms,
        RoomId room,
        HostRuleSet rules,
        BalanceConfig? balance = null,
        MovementConfig? movement = null,
        GameSessionTuning? tuning = null,
        IDomainFactory? factory = null,
        IRatingPolicy? ratingPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        var snapshot = rooms.TryGetRoom(room)
            ?? throw new InvalidOperationException($"Комната {room.Value} не найдена");
        return new GameSessionService(room, snapshot.Participants, rules, balance, movement, tuning, factory, ratingPolicy);
    }

    public RoomId Room { get; }

    public HostRuleSet Rules { get; }

    public IReadOnlyList<PlayerId> TeamAPlayers => _teamAPlayers;

    public IReadOnlyList<PlayerId> TeamBPlayers => _teamBPlayers;

    public IMatchmakingService Lobby { get; }

    public IRatingRepository Ratings { get; }

    public bool IsOver => _outcome != SessionOutcome.None;

    public SessionOutcome Outcome => _outcome;

    public Team? Winner => _winner;

    public void SetFleet(PlayerId player, IReadOnlyList<ShipUnit> ships)
    {
        ThrowIfFrozen();
        var lane = LaneOf(player);
        _orchestrator.SetFleet(lane.Match.Id, player, ships);
    }

    public MatchStarted? ConfirmFleet(PlayerId player)
    {
        ThrowIfFrozen();
        var lane = LaneOf(player);
        var started = _orchestrator.ConfirmFleet(lane.Match.Id, player);
        if (started is null)
        {
            return null; // пара ещё не дозакупилась
        }

        lane.Battle = started.Battle; // MatchStarted = сессия начинает бой лэйна
        SpawnSide(started.PlayerA, started.Battle.TeamA, homeSide: BaseId.HomeA);
        SpawnSide(started.PlayerB, started.Battle.TeamB, homeSide: BaseId.HomeB);
        return started;
    }

    public void SetPath(PlayerId player, ShipId ship, IReadOnlyList<Waypoint> path)
    {
        ThrowIfFrozen();
        var key = new MoveKey(player, ship);
        if (_spawned.ContainsKey(key))
        {
            _movement.SetPath(key, path);
            return;
        }

        throw new InvalidOperationException(
            $"Корабль {ship.Value} игрока {player.Value} не спавнился: лейн ещё не стартовал (нет MatchStarted)");
    }

    public void Tick(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(span), span, "Тик не может быть отрицательным");
        }

        if (IsOver)
        {
            return; // после исхода сессия замерзает
        }

        if (_lanes.All(lane => lane.Battle is null))
        {
            return; // бой не начинался: в лобби закуп, тикать нечего
        }

        // 1) движение всех зарегистрированных кораблей (погибших — пути сняты ниже)
        _movement.Tick(span);

        // 2) присутствие: живые корабли в зонах баз → 3) такт режима (захват/очки/таймер)
        var presence = new List<BasePresence>();
        foreach (var (key, (ship, player)) in _spawned)
        {
            if (!ship.Destroyed && _map.TryZoneOf(_movement.PositionOf(key)) is { } zone)
            {
                presence.Add(new BasePresence(zone, TeamOf(player), 1));
            }
        }

        _bases.SetPresence(presence);
        _bases.Tick(span);

        // 4) такт боя лэйнов: только когда живые корабли сторон сблизились (базы не воюют)
        foreach (var lane in _lanes)
        {
            TickLane(lane);
        }

        foreach (var dead in _spawned.Where(kv => kv.Value.Ship.Destroyed))
        {
            _movement.SetPath(dead.Key, []); // погибший корабль движение прекращает
        }

        // 5) проверка исхода
        EvaluateEnd();
    }

    public GameSessionState GetState()
    {
        var ships = _spawned
            .Select(kv =>
            {
                var (ship, player) = kv.Value;
                var key = kv.Key;
                return new SessionShipState(
                    player,
                    TeamOf(player),
                    key.Ship,
                    ship.Class,
                    _movement.PositionOf(key),
                    _movement.FuelOf(key),
                    !ship.Destroyed);
            })
            .ToList();

        var bases = new List<SessionBaseState>();
        foreach (BaseId baseId in new[] { BaseId.HomeA, BaseId.Central, BaseId.HomeB })
        {
            bases.Add(new SessionBaseState(
                baseId,
                _bases.OwnerOf(baseId),
                _bases.CapturingTeamOf(baseId),
                _bases.CaptureProgressSeconds(baseId)));
        }

        return new GameSessionState(
            _outcome,
            IsOver,
            BattleStarted: _spawned.Count > 0,
            Winner: _winner,
            Ships: ships,
            Bases: bases,
            ScoreTeamA: _bases.ScoreOf(TeamA),
            ScoreTeamB: _bases.ScoreOf(TeamB),
            HoldAllSecondsTeamA: _bases.HoldAllSecondsOf(TeamA),
            HoldAllSecondsTeamB: _bases.HoldAllSecondsOf(TeamB));
    }

    private Lane BuildLane(PlayerId playerA, PlayerId playerB)
    {
        Lobby.Join(playerA);
        var match = Lobby.Join(playerB)
            ?? throw new InvalidOperationException("Лобби не собрал пару из четырёх игроков");
        if (match.A != playerA || match.B != playerB)
        {
            throw new InvalidOperationException("Лобби собрал пару не в порядке очередности");
        }

        Lobby.Ready(match.A);
        Lobby.Ready(match.B);
        _orchestrator.Attach(match);
        return new Lane { Match = match };
    }

    private void SpawnSide(PlayerId player, Fleet fleet, BaseId homeSide)
    {
        var center = _map.CenterOf(homeSide);
        var slot = (TeamOf(player) == TeamA ? TeamAPlayers : TeamBPlayers).ToList().IndexOf(player);
        var playerOffset = (slot - 0.5) * 2 * _tuning.SpawnOffsetPerPlayer; // дефолт-рассадка
        for (var i = 0; i < fleet.Ships.Count; i++)
        {
            var ship = fleet.Ships[i];
            var key = new MoveKey(player, ship.Id);
            var shipSpread = (i - (fleet.Ships.Count - 1) / 2.0) * _tuning.SpawnSpreadPerShip;
            _spawned.Add(key, (ship, player)); // дубликат ShipId у игрока — ошибка тут же
            _movement.RegisterShip(key, ship.Class, center + new ShipPosition(playerOffset, shipSpread));
        }
    }

    private void TickLane(Lane lane)
    {
        var battle = lane.Battle!;
        if (battle.IsOver || !AnySideInRange(lane))
        {
            return; // триггер дальности: вне WeaponRange такт боя пуст
        }

        _combat.Tick(battle);
    }

    private bool AnySideInRange(Lane lane)
    {
        var sideA = AlivePositions(lane.PlayerA);
        var sideB = AlivePositions(lane.PlayerB);
        return sideA.Any(positionA =>
            sideB.Any(positionB => positionA.DistanceTo(positionB) <= _tuning.WeaponRange));
    }

    private IEnumerable<ShipPosition> AlivePositions(PlayerId owner) =>
        _spawned
            .Where(kv => kv.Value.Player == owner && !kv.Value.Ship.Destroyed)
            .Select(kv => _movement.PositionOf(kv.Key));

    /// <summary>
    /// Исходы: обе стороны уничтожены в один тик → ничья; одна сторона уничтожена → победа другой;
    /// победа по базам (таймер/очки); все лэйны доиграны при живых сторонах: счёт лэйнов 1:1 —
    /// «одинаковое уничтожение» → ничья. Исход фиксируется один раз, далее — замерзание.
    /// </summary>
    private void EvaluateEnd()
    {
        if (_lanes.Any(lane => lane.Battle is null))
        {
            return; // исходы считаем только когда оба лэйна стартовали
        }

        var spawnedA = SpawnedCount(isTeamA: true);
        var spawnedB = SpawnedCount(isTeamA: false);
        if (spawnedA == 0 || spawnedB == 0)
        {
            return; // сторона без спавна: исход не считаем (нечестный матч)
        }

        var aliveA = AliveCount(isTeamA: true);
        var aliveB = AliveCount(isTeamA: false);
        if (aliveA == 0 && aliveB == 0)
        {
            Finish(SessionOutcome.Draw); // обе стороны уничтожены в один тик
            return;
        }

        if (aliveA == 0)
        {
            Finish(SessionOutcome.TeamBWon);
            return;
        }

        if (aliveB == 0)
        {
            Finish(SessionOutcome.TeamAWon);
            return;
        }

        if (_bases.IsOver)
        {
            Finish(_bases.Winner == TeamA ? SessionOutcome.TeamAWon : SessionOutcome.TeamBWon);
            return;
        }

        // все лэйны доиграны при живых обеих сторонах: счёт лэйнов 1:1 — «одинаковое уничтожение» → ничья
        var decided = _lanes.Where(lane => lane.Battle!.IsOver).ToList();
        if (decided.Count < _lanes.Count)
        {
            return; // хотя бы один лэйн ещё дерётся — исхода нет
        }

        var laneWinsA = decided.Count(lane => !lane.Battle!.TeamA.IsWiped);
        Finish(laneWinsA switch
        {
            0 => SessionOutcome.TeamBWon,
            1 => SessionOutcome.Draw,
            _ => SessionOutcome.TeamAWon,
        });
    }

    private void Finish(SessionOutcome outcome)
    {
        if (IsOver)
        {
            return; // исход один раз
        }

        _outcome = outcome;
        _winner = outcome switch
        {
            SessionOutcome.TeamAWon => TeamA,
            SessionOutcome.TeamBWon => TeamB,
            _ => null, // ничья — победителя нет
        };

        RecordRatings(outcome);
    }

    /// <summary>
    /// Рейтинг после решённого исхода (победа одной из сторон): по умолчанию — прежняя плоская
    /// схема IRatingRepository, одна запись на пару победитель↔проигравший (у каждого игрока
    /// ровно один +15/−10 и один счётчик). При подключённой IRatingPolicy — политика считает
    /// рейтинги ВСЕХ игроков по составам сторон. Ничья рейтингом не пишется в обоих режимах
    /// (дефолт «0/0» сохранён; S ничьей остаётся в самой политике, EloConfig.DrawScore).
    /// </summary>
    private void RecordRatings(SessionOutcome outcome)
    {
        if (outcome is not (SessionOutcome.TeamAWon or SessionOutcome.TeamBWon))
        {
            return;
        }

        var winners = outcome == SessionOutcome.TeamAWon ? _teamAPlayers : _teamBPlayers;
        var losers = outcome == SessionOutcome.TeamAWon ? _teamBPlayers : _teamAPlayers;
        if (_policyRatings is not null)
        {
            var winnerSide = outcome == SessionOutcome.TeamAWon ? RatingGameWinner.TeamA : RatingGameWinner.TeamB;
            _policyRatings.Apply(new RatingGameResult(winners, losers, winnerSide));
            return;
        }

        for (var i = 0; i < Math.Min(winners.Count, losers.Count); i++)
        {
            Ratings.RecordOutcome(new BattleOutcome(winners[i], losers[i]));
        }
    }

    private Lane LaneOf(PlayerId player) =>
        _lanes.FirstOrDefault(lane => lane.PlayerA == player || lane.PlayerB == player)
            ?? throw new InvalidOperationException($"Игрок {player.Value} не участник этой сессии");

    private Team TeamOf(PlayerId player) => _isTeamAByPlayer[player] ? TeamA : TeamB;

    private int SpawnedCount(bool isTeamA) =>
        _spawned.Count(kv => _isTeamAByPlayer[kv.Value.Player] == isTeamA);

    private int AliveCount(bool isTeamA) =>
        _spawned.Count(kv => _isTeamAByPlayer[kv.Value.Player] == isTeamA && !kv.Value.Ship.Destroyed);

    private void ThrowIfFrozen()
    {
        if (IsOver)
        {
            throw new InvalidOperationException("Сессия завершена: команды после исхода не принимаются");
        }
    }
}
