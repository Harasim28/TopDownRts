namespace Rts.Domain;

/// <summary>Тип режима, который хост комнаты выбирает в конструкторе (кадр 2).</summary>
public enum Mode
{
    /// <summary>Базовый захват (кадр 4a): три базы, очки за удержание, таймер владения всеми базами.</summary>
    BaseControl,
}

/// <summary>
/// Ползунок правила хоста: допустимый диапазон Min..Max и заводское Default.
/// Значение, выбранное хостом, проверяется этим диапазоном.
/// </summary>
public readonly record struct Slider(int Min, int Max, int Default)
{
    /// <summary>Проверить значение; вне диапазона — InvalidOperationException с понятным сообщением.</summary>
    public int EnsureValid(int value, string fieldName)
    {
        if (value < Min || value > Max)
        {
            throw new InvalidOperationException(
                $"{fieldName}={value} вне диапазона ползунка [{Min}..{Max}] (по умолчанию {Default}).");
        }

        return value;
    }
}

/// <summary>
/// Правила режима, которые хост комнаты задаёт ползунками (кадр 2): тип режима + значения слайдеров.
/// Все числа режима живут ТОЛЬКО здесь. Дефолты — по аналогии с доминацией/завоеванием (WarThunder)
/// и НЕ подтверждены владельцем; HoldAllBasesMinutes = 10 минут — пример владельца от 05.11
/// (удержание ВСЕХ баз суммарно 10 минут = победа), тоже пока не подтверждён.
/// </summary>
public sealed class HostRuleSet
{
    /// <summary>Заводские дефолты: единый источник чисел для слайдеров, конструктора и Default().</summary>
    public const int DefaultHoldAllBasesMinutes = 10;
    public const int DefaultVictoryScoreThreshold = 600;
    public const int DefaultOwnBaseScorePerMinute = 30;
    public const int DefaultCentralBaseScorePerMinute = 60;
    public const int DefaultEnemyBaseScorePerMinute = 30;
    public const int DefaultCaptureSeconds = 20;

    // --- Ползунки (Slider: Min/Max/Default); диапазоны — тоже дефолтные, подтверждаются владельцем ---

    /// <summary>Минут непрерывного владения ВСЕМИ тремя базами до победы. 10 — пример владельца (05.11), не подтверждено.</summary>
    public static readonly Slider HoldAllBasesMinutesSlider = new(Min: 1, Max: 60, Default: DefaultHoldAllBasesMinutes);

    /// <summary>Порог очков: победа по очкам раньше таймера.</summary>
    public static readonly Slider VictoryScoreThresholdSlider = new(Min: 100, Max: 2000, Default: DefaultVictoryScoreThreshold);

    /// <summary>Очки в минуту за удержание своей (домашней) базы.</summary>
    public static readonly Slider OwnBaseScorePerMinuteSlider = new(Min: 1, Max: 180, Default: DefaultOwnBaseScorePerMinute);

    /// <summary>Очки в минуту за удержание центральной базы.</summary>
    public static readonly Slider CentralBaseScorePerMinuteSlider = new(Min: 1, Max: 240, Default: DefaultCentralBaseScorePerMinute);

    /// <summary>Очки в минуту за удержание вражеской (домашней базы противника).</summary>
    public static readonly Slider EnemyBaseScorePerMinuteSlider = new(Min: 1, Max: 180, Default: DefaultEnemyBaseScorePerMinute);

    /// <summary>Секунд непрерывного присутствия одной команды в зоне базы для захвата.</summary>
    public static readonly Slider CaptureSecondsSlider = new(Min: 5, Max: 60, Default: DefaultCaptureSeconds);

    /// <summary>Тип режима, заданный хостом.</summary>
    public Mode Mode { get; }

    public int HoldAllBasesMinutes { get; }
    public int VictoryScoreThreshold { get; }
    public int OwnBaseScorePerMinute { get; }
    public int CentralBaseScorePerMinute { get; }
    public int EnemyBaseScorePerMinute { get; }
    public int CaptureSeconds { get; }

    /// <summary>Значения ползунков для режима BaseControl; незаданные — заводские дефолты.</summary>
    public HostRuleSet(
        int holdAllBasesMinutes = DefaultHoldAllBasesMinutes,
        int victoryScoreThreshold = DefaultVictoryScoreThreshold,
        int ownBaseScorePerMinute = DefaultOwnBaseScorePerMinute,
        int centralBaseScorePerMinute = DefaultCentralBaseScorePerMinute,
        int enemyBaseScorePerMinute = DefaultEnemyBaseScorePerMinute,
        int captureSeconds = DefaultCaptureSeconds)
        : this(Mode.BaseControl, holdAllBasesMinutes, victoryScoreThreshold,
            ownBaseScorePerMinute, centralBaseScorePerMinute, enemyBaseScorePerMinute, captureSeconds)
    {
    }

    /// <summary>Тип режима + значения ползунков; значение вне диапазона — InvalidOperationException.</summary>
    public HostRuleSet(
        Mode mode,
        int holdAllBasesMinutes = DefaultHoldAllBasesMinutes,
        int victoryScoreThreshold = DefaultVictoryScoreThreshold,
        int ownBaseScorePerMinute = DefaultOwnBaseScorePerMinute,
        int centralBaseScorePerMinute = DefaultCentralBaseScorePerMinute,
        int enemyBaseScorePerMinute = DefaultEnemyBaseScorePerMinute,
        int captureSeconds = DefaultCaptureSeconds)
    {
        if (mode != Mode.BaseControl)
        {
            throw new InvalidOperationException(
                $"Режим {mode} каркасом пока не поддержан: задавайте Mode.BaseControl.");
        }

        Mode = mode;
        HoldAllBasesMinutes = HoldAllBasesMinutesSlider.EnsureValid(holdAllBasesMinutes, nameof(holdAllBasesMinutes));
        VictoryScoreThreshold = VictoryScoreThresholdSlider.EnsureValid(victoryScoreThreshold, nameof(victoryScoreThreshold));
        OwnBaseScorePerMinute = OwnBaseScorePerMinuteSlider.EnsureValid(ownBaseScorePerMinute, nameof(ownBaseScorePerMinute));
        CentralBaseScorePerMinute = CentralBaseScorePerMinuteSlider.EnsureValid(centralBaseScorePerMinute, nameof(centralBaseScorePerMinute));
        EnemyBaseScorePerMinute = EnemyBaseScorePerMinuteSlider.EnsureValid(enemyBaseScorePerMinute, nameof(enemyBaseScorePerMinute));
        CaptureSeconds = CaptureSecondsSlider.EnsureValid(captureSeconds, nameof(captureSeconds));
    }

    /// <summary>Заводские дефолты (WT-аналогия; не подтверждены владельцем, кроме 10 минут — пример владельца).</summary>
    public static HostRuleSet Default() => new(Mode.BaseControl);
}
