namespace Rts.Domain;

/// <summary>
/// In-memory сервис комнат (кадр 1): состояние в памяти процесса, никакого времени/сети/БД.
/// Правило лобби (Ready и его таймаут) в IMatchmakingService — здесь не дублируется.
/// </summary>
public class RoomService : IRoomService
{
    private readonly List<RoomState> _rooms = new();
    private readonly List<InviteState> _invites = new();
    private int _nextRoomNumber;
    private int _nextInviteNumber;

    private sealed class RoomState
    {
        public RoomId Id;
        public PlayerId Host;
        public RoomLimits Limits = new();
        public RoomStatus Status = RoomStatus.Open;
        public List<PlayerId> Participants = new();

        public Room Snapshot() => new(Id, Host, Limits, Status, Participants.ToList());
    }

    private sealed class InviteState
    {
        public InviteId Id;
        public RoomId Room;
        public PlayerId From;
        public PlayerId To;
        public RoomInviteStatus Status = RoomInviteStatus.Pending;

        public RoomInvite Snapshot() => new(Id, Room, From, To, Status);
    }

    public RoomCreated CreateRoom(PlayerId host)
    {
        EnsurePlayerFree(host, excludeRoom: null);
        _nextRoomNumber++;
        var room = new RoomState
        {
            Id = new RoomId($"r{_nextRoomNumber}"),
            Host = host,
        };
        room.Participants.Add(host);
        _rooms.Add(room);
        return new RoomCreated(room.Snapshot());
    }

    public void Join(RoomId room, PlayerId player)
    {
        var state = GetOpenRoom(room);
        if (state.Participants.Contains(player))
        {
            throw new InvalidOperationException($"Игрок {player.Value} уже участник комнаты {room.Value}");
        }

        EnsurePlayerFree(player, room);
        AddMember(state, player);
    }

    public void Leave(RoomId room, PlayerId player)
    {
        var state = GetOpenRoom(room);
        if (player == state.Host)
        {
            throw new InvalidOperationException(
                $"Хост не может покинуть комнату {room.Value}: только Close, передача хоста не предполагается");
        }

        if (!state.Participants.Remove(player))
        {
            throw new InvalidOperationException($"Игрок {player.Value} не участник комнаты {room.Value}");
        }
    }

    public InviteSent Invite(RoomId room, PlayerId from, PlayerId to)
    {
        var state = GetOpenRoom(room);
        if (!state.Participants.Contains(from))
        {
            throw new InvalidOperationException($"Приглашать в комнату {room.Value} может только участник");
        }

        if (state.Participants.Contains(to))
        {
            throw new InvalidOperationException($"Игрок {to.Value} уже участник комнаты {room.Value}");
        }

        _nextInviteNumber++;
        var invite = new InviteState
        {
            Id = new InviteId($"i{_nextInviteNumber}"),
            Room = room,
            From = from,
            To = to,
        };
        _invites.Add(invite);
        return new InviteSent(invite.Snapshot());
    }

    public Joined Accept(RoomId room, PlayerId player)
    {
        var state = GetOpenRoom(room);
        if (state.Participants.Contains(player))
        {
            throw new InvalidOperationException($"Игрок {player.Value} уже участник комнаты {room.Value}");
        }

        var invite = _invites.FirstOrDefault(i =>
                i.Room == room && i.To == player && i.Status == RoomInviteStatus.Pending)
            ?? throw new InvalidOperationException(
                $"Нет активного приглашения в комнату {room.Value} для игрока {player.Value}");

        EnsurePlayerFree(player, room);
        AddMember(state, player);
        invite.Status = RoomInviteStatus.Accepted;
        return new Joined(room, player, state.Participants.ToList());
    }

    public void Kick(RoomId room, PlayerId host, PlayerId player)
    {
        var state = GetOpenRoom(room);
        EnsureHost(state, host, "Кикнуть");
        if (player == state.Host)
        {
            throw new InvalidOperationException(
                $"Хост не может кикнуть себя из комнаты {room.Value}: только Close");
        }

        if (!state.Participants.Remove(player))
        {
            throw new InvalidOperationException($"Игрок {player.Value} не участник комнаты {room.Value}");
        }
    }

    public RoomStarted Start(RoomId room, PlayerId host)
    {
        var state = GetOpenRoom(room);
        EnsureHost(state, host, "Стартовать");
        if (state.Participants.Count < 2)
        {
            throw new InvalidOperationException(
                $"Комната {room.Value}: для старта нужно минимум два участника");
        }

        return new RoomStarted(room, state.Participants.ToList());
    }

    public void Close(RoomId room, PlayerId host)
    {
        var state = GetOpenRoom(room);
        EnsureHost(state, host, "Закрыть");
        state.Status = RoomStatus.Closed;
    }

    public Room? TryGetRoom(RoomId room) => _rooms.FirstOrDefault(r => r.Id == room)?.Snapshot();

    private RoomState GetOpenRoom(RoomId room)
    {
        var state = _rooms.FirstOrDefault(r => r.Id == room)
            ?? throw new InvalidOperationException($"Комната {room.Value} не найдена");
        if (state.Status != RoomStatus.Open)
        {
            throw new InvalidOperationException($"Комната {room.Value} закрыта");
        }

        return state;
    }

    private void AddMember(RoomState state, PlayerId player)
    {
        if (state.Participants.Count >= state.Limits.Capacity)
        {
            throw new InvalidOperationException(
                $"Комната {state.Id.Value} заполнена: капа {state.Limits.Capacity} участников");
        }

        state.Participants.Add(player);
    }

    /// <summary>Один игрок — не больше одной открытой комнаты (кроме комнаты excludeRoom).</summary>
    private void EnsurePlayerFree(PlayerId player, RoomId? excludeRoom)
    {
        var busy = _rooms.FirstOrDefault(r =>
            r.Status == RoomStatus.Open && r.Id != excludeRoom && r.Participants.Contains(player));
        if (busy is not null)
        {
            throw new InvalidOperationException(
                $"Игрок {player.Value} уже состоит в открытой комнате {busy.Id.Value}");
        }
    }

    private static void EnsureHost(RoomState state, PlayerId caller, string action)
    {
        if (caller != state.Host)
        {
            throw new InvalidOperationException(
                $"{action} в комнате {state.Id.Value} может только хост");
        }
    }
}
