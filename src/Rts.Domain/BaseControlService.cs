namespace Rts.Domain;

/// <summary>
/// Режим «базовый захват» (кадр 4a), всё тиками — без реального времени, сетей и таймеров.
/// 1) Захват: в зоне базы только команда X ≠ владелец, непрерывно CaptureSeconds — база переходит X;
///    зона оспорена (≥2 команды) или пуста — прогресс захвата сбрасывается.
/// 2) Очки: каждая база во владении даёт владельцу очки тиками по профилю базы
///    (своя / центральная / вражеская) × минуты тика.
/// 3) Таймер победы: непрерывное владение ВСЕМИ ТРЕМЯ базами одной командой копит секунды;
///    потеря любой базы — сброс серии.
/// 4) Победа: таймер ≥ HoldAllBasesMinutes ИЛИ очки ≥ VictoryScoreThreshold; после победы тики ничего не меняют.
/// Присутствие передаётся напрямую (BasePresence: база, команда, число живых) — координатной механики
/// движения в кадре нет. Стартовая расстановка: дома за своими командами, центр нейтральный.
/// Все числа — из HostRuleSet (правила задаёт хост комнаты).
/// </summary>
public sealed class BaseControlService : IBaseControlService
{
    private static readonly Team TeamA = new(0);
    private static readonly Team TeamB = new(1);

    private const int BaseCount = 3; // BaseId: HomeA, Central, HomeB

    private readonly HostRuleSet _rules;
    private readonly BaseState[] _bases = new BaseState[BaseCount]; // индекс = (int)BaseId
    private readonly Dictionary<Team, double> _score = new();
    private readonly Dictionary<Team, double> _holdAllSeconds = new();
    private Dictionary<BaseId, Dictionary<Team, int>> _presence = new();
    private bool _isOver;
    private Team? _winner;

    private sealed class BaseState
    {
        public Team? Owner;
        public double CaptureProgressSeconds;
        public Team? CapturingTeam;
    }

    public BaseControlService(HostRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _bases[(int)BaseId.HomeA] = new BaseState { Owner = TeamA };  // свой дом — сразу у своей команды
        _bases[(int)BaseId.Central] = new BaseState { Owner = null }; // центр — нейтральный
        _bases[(int)BaseId.HomeB] = new BaseState { Owner = TeamB };
    }

    public void Tick(TimeSpan span)
    {
        if (_isOver)
        {
            return; // победа зафиксирована: очки, таймер и владение не растут
        }

        if (span < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(span), span, "Такт не может быть отрицательным.");
        }

        // Владение считается по состоянию НА начало тика: очки капают за удержание баз, затем
        // отрабатывают захваты. Серия владения всеми базами пишется тику, только если команда
        // владела всеми тремя и в начале, и в конце тика: захват третьей базы начинает серию
        // со следующего тика, потеря любой базы обнуляет серию тем же тиком.
        AccrueScores(span);
        var heldAllAtStart = OwnerTeams().Where(OwnsAllBases).ToHashSet();
        ProcessCaptures(span);
        TrackHoldAll(heldAllAtStart, span);
        CheckVictory();
    }

    public void SetPresence(IReadOnlyCollection<BasePresence> presence)
    {
        ArgumentNullException.ThrowIfNull(presence);

        var aggregated = new Dictionary<BaseId, Dictionary<Team, int>>();
        foreach (var entry in presence)
        {
            if (entry.AliveShips <= 0)
            {
                continue; // мёртвые корабли не создают присутствия
            }

            if (!aggregated.TryGetValue(entry.Base, out var perTeam))
            {
                perTeam = new Dictionary<Team, int>();
                aggregated[entry.Base] = perTeam;
            }

            perTeam[entry.Team] = perTeam.GetValueOrDefault(entry.Team) + entry.AliveShips;
        }

        _presence = aggregated; // снимок действует до следующего вызова
    }

    public Team? OwnerOf(BaseId baseId) => State(baseId).Owner;

    public double CaptureProgressSeconds(BaseId baseId) => State(baseId).CaptureProgressSeconds;

    public Team? CapturingTeamOf(BaseId baseId) => State(baseId).CapturingTeam;

    public double ScoreOf(Team team) => _score.GetValueOrDefault(team);

    public double HoldAllSecondsOf(Team team) => _holdAllSeconds.GetValueOrDefault(team);

    public bool IsOver => _isOver;

    public Team? Winner => _winner;

    private BaseState State(BaseId baseId) => _bases[(int)baseId];

    /// <summary>Захват по присутствию: в зоне ровно одна команда ≠ владелец — прогресс растёт непрерывно.</summary>
    private void ProcessCaptures(TimeSpan span)
    {
        foreach (BaseId id in AllBases())
        {
            var state = State(id);
            var teamsInZone = TeamsInZone(id);

            if (teamsInZone.Count == 1 && state.Owner != teamsInZone[0])
            {
                state.CaptureProgressSeconds += span.TotalSeconds;
                state.CapturingTeam = teamsInZone[0];

                if (state.CaptureProgressSeconds >= _rules.CaptureSeconds)
                {
                    state.Owner = teamsInZone[0];
                    state.CaptureProgressSeconds = 0;
                    state.CapturingTeam = null;
                }
            }
            else
            {
                // владелец сам в зоне, зона оспорена или пуста — захвата нет, прогресс сгорает
                state.CaptureProgressSeconds = 0;
                state.CapturingTeam = null;
            }
        }
    }

    /// <summary>Очки за владение: каждая занятая база даёт владельцу очки по профилю × минуты тика.</summary>
    private void AccrueScores(TimeSpan span)
    {
        var minutes = span.TotalMinutes;
        foreach (BaseId id in AllBases())
        {
            if (State(id).Owner is { } owner)
            {
                _score[owner] = ScoreOf(owner) + ScorePerMinuteFor(id, owner) * minutes;
            }
        }
    }

    /// <summary>Очки в минуту базы для команды-владельца: своя / центральная / вражеская (из HostRuleSet).</summary>
    private int ScorePerMinuteFor(BaseId baseId, Team owner)
    {
        return baseId switch
        {
            BaseId.Central => _rules.CentralBaseScorePerMinute,
            BaseId.HomeA => owner == TeamA ? _rules.OwnBaseScorePerMinute : _rules.EnemyBaseScorePerMinute,
            BaseId.HomeB => owner == TeamB ? _rules.OwnBaseScorePerMinute : _rules.EnemyBaseScorePerMinute,
            _ => throw new ArgumentOutOfRangeException(nameof(baseId), baseId, "Неизвестная база."),
        };
    }

    /// <summary>
    /// Таймер владения всеми тремя базами: тик копит серию только при полном владении и в начале,
    /// и в конце тика (heldAllAtStart + состояние после захватов); иначе серия сбрасывается в ноль.
    /// </summary>
    private void TrackHoldAll(HashSet<Team> heldAllAtStart, TimeSpan span)
    {
        foreach (var team in _holdAllSeconds.Keys.ToList())
        {
            if (!heldAllAtStart.Contains(team) || !OwnsAllBases(team))
            {
                _holdAllSeconds[team] = 0;
            }
        }

        foreach (var team in OwnerTeams())
        {
            if (heldAllAtStart.Contains(team) && OwnsAllBases(team))
            {
                _holdAllSeconds[team] = HoldAllSecondsOf(team) + span.TotalSeconds;
            }
        }
    }

    /// <summary>Победа: таймер непрерывного владения всеми базами ИЛИ порог очков.</summary>
    private void CheckVictory()
    {
        foreach (var (team, holdSeconds) in _holdAllSeconds)
        {
            if (holdSeconds >= _rules.HoldAllBasesMinutes * 60)
            {
                DeclareWinner(team);
                return;
            }
        }

        foreach (var (team, score) in _score)
        {
            if (score >= _rules.VictoryScoreThreshold)
            {
                DeclareWinner(team);
                return;
            }
        }
    }

    private void DeclareWinner(Team team)
    {
        if (_isOver)
        {
            return;
        }

        _isOver = true;
        _winner = team;
    }

    private static List<BaseId> AllBases() => new() { BaseId.HomeA, BaseId.Central, BaseId.HomeB };

    private List<Team> TeamsInZone(BaseId baseId) =>
        _presence.TryGetValue(baseId, out var perTeam) ? perTeam.Keys.ToList() : new List<Team>();

    private List<Team> OwnerTeams() =>
        _bases.Select(b => b.Owner).Where(t => t is not null).Select(t => t!.Value).Distinct().ToList();

    private bool OwnsAllBases(Team team) => _bases.All(b => b.Owner == team);
}
