namespace Rts.Domain;

/// <summary>Три зоны захвата (кадр 4a): дома команд и центр.</summary>
public enum BaseId
{
    HomeA = 0,   // дом команды A
    Central = 1, // центральная база
    HomeB = 2,   // дом команды B
}

/// <summary>
/// Присутствие в зоне базы: сколько живых кораблей команды стоят в зоне.
/// Позиции передаются вызывающим напрямую (кто из живых кораблей какой команды в зоне уже
/// разрешён в тройку «база, команда, число живых») — координатная механика движения в кадр 4a
/// не входит. Присутствие живёт тиками вместе с Tick, без реального времени.
/// </summary>
public readonly record struct BasePresence(BaseId Base, Team Team, int AliveShips);

/// <summary>
/// Контракт режима «базовый захват» (кадр 4a). Владение базой — Team A или B; нейтральная база — null.
/// Правила и все числа режима — в <see cref="HostRuleSet"/>, который задаёт хост комнаты.
/// Ничего реального времени: только тики (Tick), как в бою.
/// </summary>
public interface IBaseControlService
{
    /// <summary>Один тик режима: обработать текущее присутствие (захват), очки, таймер всех баз.</summary>
    void Tick(TimeSpan span);

    /// <summary>
    /// Обновить присутствие: кто из живых кораблей какой команды стоит в зонах баз.
    /// Снимок действует до следующего вызова (непрерывность присутствия — серия тиков без нового вызова).
    /// </summary>
    void SetPresence(IReadOnlyCollection<BasePresence> presence);

    /// <summary>Владелец базы: Team A/B, null — нейтральная.</summary>
    Team? OwnerOf(BaseId baseId);

    /// <summary>Набранный прогресс захвата базы в секундах (сбрасывается при оспаривании/пустой зоне).</summary>
    double CaptureProgressSeconds(BaseId baseId);

    /// <summary>Кто сейчас копит захват базы (null — захвата нет).</summary>
    Team? CapturingTeamOf(BaseId baseId);

    /// <summary>Суммарные очки команды за удержание баз (тикают по профилю базы из HostRuleSet).</summary>
    double ScoreOf(Team team);

    /// <summary>Секунды НЕПРЕРЫВНОГО владения всеми тремя базами (потеря любой базы — сброс).</summary>
    double HoldAllSecondsOf(Team team);

    /// <summary>Победа: таймер всех баз ≥ HoldAllBasesMinutes ИЛИ очки ≥ VictoryScoreThreshold.</summary>
    bool IsOver { get; }

    /// <summary>Победитель; null, пока матч идёт.</summary>
    Team? Winner { get; }
}
