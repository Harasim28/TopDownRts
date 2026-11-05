namespace Rts.Domain;

// ---- Кадр 1 «Сервис комнат и приглашений» (сводка созвона 24.09) ----

public readonly record struct RoomId(string Value);

public readonly record struct InviteId(string Value);

/// <summary>
/// Лимиты комнаты: просто поля с дефолтами — slider-подход не нужен (фиксация сверки 24.09).
/// </summary>
public record RoomLimits
{
    /// <summary>Максимум участников комнаты по умолчанию, включая хоста.</summary>
    public const int DefaultCapacity = 4;

    /// <summary>Максимум участников комнаты, включая хоста.</summary>
    public int Capacity { get; init; } = DefaultCapacity;
}

/// <summary>Состояние комнаты.</summary>
public enum RoomStatus
{
    Open,
    Closed,
}

/// <summary>Статус приглашения в комнату.</summary>
public enum RoomInviteStatus
{
    Pending,
    Accepted,
}

/// <summary>Комната открытого входа: хост — первый участник, остальные входят или приглашаются.</summary>
public record Room(RoomId Id, PlayerId HostPlayerId, RoomLimits Limits, RoomStatus Status, IReadOnlyList<PlayerId> Participants)
{
    /// <summary>Состоит ли игрок в комнате.</summary>
    public bool HasMember(PlayerId player) => Participants.Contains(player);
}

/// <summary>Приглашение в комнату от участника (хоста или члена) к игроку.</summary>
public record RoomInvite(InviteId Id, RoomId Room, PlayerId From, PlayerId To, RoomInviteStatus Status);

// ---- События сервиса комнат ----

/// <summary>Комната создана: хост — первый участник.</summary>
public record RoomCreated(Room Room);

/// <summary>Приглашение отправлено участником комнаты.</summary>
public record InviteSent(RoomInvite Invite);

/// <summary>Игрок вошёл в комнату через приглашение — в пределах капы.</summary>
public record Joined(RoomId Room, PlayerId Player, IReadOnlyList<PlayerId> Participants);

/// <summary>Хост стартовал комнату при минимум двух участниках.</summary>
public record RoomStarted(RoomId Room, IReadOnlyList<PlayerId> Participants);

/// <summary>
/// Сервис комнат (кадр 1). Правила:
/// — один игрок состоит не больше чем в одной открытой комнате;
/// — дубликат Join отклонён; Accept без/с чужим/повторным приглашением отклонён;
/// — Invite — только участник; Kick/Start/Close — только хост;
/// — хост не может покинуть комнату: только Close (передача хоста не предполагается);
/// — Start: минимум два участника;
/// — комнаты — надстройка над лобби: правило Ready с таймаутом живёт в IMatchmakingService и здесь не дублируется.
/// </summary>
public interface IRoomService
{
    /// <summary>Игрок создаёт комнату и становится её хостом и первым участником.</summary>
    RoomCreated CreateRoom(PlayerId host);

    /// <summary>Игрок входит в открытую комнату (в пределах капы, не больше одной открытой комнаты).</summary>
    void Join(RoomId room, PlayerId player);

    /// <summary>Участник покидает комнату; хост не может — только Close.</summary>
    void Leave(RoomId room, PlayerId player);

    /// <summary>Участник приглашает игрока в комнату.</summary>
    InviteSent Invite(RoomId room, PlayerId from, PlayerId to);

    /// <summary>Приглашённый игрок принимает своё активное приглашение и входит (в пределах капы).</summary>
    Joined Accept(RoomId room, PlayerId player);

    /// <summary>Хост выгоняет участника из комнаты.</summary>
    void Kick(RoomId room, PlayerId host, PlayerId player);

    /// <summary>Хост стартует комнату: минимум два участника.</summary>
    RoomStarted Start(RoomId room, PlayerId host);

    /// <summary>Хост закрывает комнату; участники снова свободны для других комнат.</summary>
    void Close(RoomId room, PlayerId host);

    /// <summary>Текущий снимок комнаты или null, если комнаты нет.</summary>
    Room? TryGetRoom(RoomId room);
}
