namespace Rts.Domain.Tests;

/// <summary>
/// Кадр 1 «Сервис комнат и приглашений» (сводка созвона 24.09): игрок создаёт комнату,
/// другие входят/приглашаются, старт — только хост с двумя+ участниками.
/// Правило лобби (Ready и его таймаут) НЕ дублируется — оно в IMatchmakingService.
/// </summary>
public class RoomTests
{
    private static readonly PlayerId Host = new("host");
    private static readonly PlayerId Alice = new("alice");
    private static readonly PlayerId Bob = new("bob");
    private static readonly PlayerId Carol = new("carol");
    private static readonly PlayerId Dave = new("dave");
    private static readonly PlayerId Outsider = new("outsider");

    private static IRoomService NewRooms() => new RoomService();

    private static RoomId NewRoom(IRoomService rooms, PlayerId? host = null)
    {
        var created = rooms.CreateRoom(host ?? Host);
        return created.Room.Id;
    }

    [Fact]
    public void Хост_создаёт_комнату()
    {
        var rooms = NewRooms();

        var created = rooms.CreateRoom(Host);

        Assert.Equal(new RoomId("r1"), created.Room.Id);
        Assert.Equal(Host, created.Room.HostPlayerId);
        Assert.Equal(RoomStatus.Open, created.Room.Status);
        Assert.Equal(RoomLimits.DefaultCapacity, created.Room.Limits.Capacity);
        Assert.Equal(new[] { Host }, created.Room.Participants);
    }

    [Fact]
    public void Игрок_входит_в_комнату()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);

        rooms.Join(room, Alice);

        Assert.Equal(new[] { Host, Alice }, rooms.TryGetRoom(room)!.Participants);
    }

    [Fact]
    public void Повторный_вход_в_комнату_отказ()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Join(room, Alice);

        Assert.Throws<InvalidOperationException>(() => rooms.Join(room, Alice));

        Assert.Equal(2, rooms.TryGetRoom(room)!.Participants.Count);
    }

    [Fact]
    public void Игрок_не_может_состоять_в_двух_открытых_комнатах()
    {
        var rooms = NewRooms();
        var first = NewRoom(rooms);
        rooms.Join(first, Alice);
        var second = NewRoom(rooms, Bob);

        Assert.Throws<InvalidOperationException>(() => rooms.Join(second, Alice));
    }

    [Fact]
    public void Вход_сверх_капы_четыре_отказ()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Join(room, Alice);
        rooms.Join(room, Bob);
        rooms.Join(room, Carol);
        Assert.Equal(4, rooms.TryGetRoom(room)!.Participants.Count);

        Assert.Throws<InvalidOperationException>(() => rooms.Join(room, Dave));

        Assert.Equal(4, rooms.TryGetRoom(room)!.Participants.Count);
    }

    [Fact]
    public void Пригласить_в_комнату_может_только_участник()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);

        Assert.Throws<InvalidOperationException>(() => rooms.Invite(room, Outsider, Bob));

        var sent = rooms.Invite(room, Host, Bob);
        Assert.Equal(Bob, sent.Invite.To);
        Assert.Equal(RoomInviteStatus.Pending, sent.Invite.Status);
    }

    [Fact]
    public void Игрок_принимает_приглашение_и_входит()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Invite(room, Host, Bob);

        var joined = rooms.Accept(room, Bob);

        Assert.Equal(room, joined.Room);
        Assert.Equal(Bob, joined.Player);
        Assert.Equal(new[] { Host, Bob }, joined.Participants);
        Assert.Equal(new[] { Host, Bob }, rooms.TryGetRoom(room)!.Participants);
    }

    [Fact]
    public void Повторный_приём_приглашения_отказ()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Invite(room, Host, Bob);
        rooms.Accept(room, Bob);

        Assert.Throws<InvalidOperationException>(() => rooms.Accept(room, Bob));

        Assert.Equal(2, rooms.TryGetRoom(room)!.Participants.Count);
    }

    [Fact]
    public void Приём_чужого_несуществующего_приглашения_отказ()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Invite(room, Host, Bob);

        Assert.Throws<InvalidOperationException>(() => rooms.Accept(room, Outsider));

        Assert.DoesNotContain(Outsider, rooms.TryGetRoom(room)!.Participants);
    }

    [Fact]
    public void Кикнуть_может_только_хост()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Join(room, Alice);
        rooms.Join(room, Bob);

        Assert.Throws<InvalidOperationException>(() => rooms.Kick(room, Alice, Bob));
        Assert.Equal(3, rooms.TryGetRoom(room)!.Participants.Count);

        rooms.Kick(room, Host, Bob);
        Assert.Equal(new[] { Host, Alice }, rooms.TryGetRoom(room)!.Participants);
    }

    [Fact]
    public void Старт_не_хостом_отказ()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Join(room, Alice);

        Assert.Throws<InvalidOperationException>(() => rooms.Start(room, Alice));
    }

    [Fact]
    public void Старт_при_одном_участнике_отказ()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);

        Assert.Throws<InvalidOperationException>(() => rooms.Start(room, Host));
    }

    [Fact]
    public void Хост_стартует_комнату_с_двумя_участниками()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Join(room, Alice);

        var started = rooms.Start(room, Host);

        Assert.Equal(room, started.Room);
        Assert.Equal(new[] { Host, Alice }, started.Participants);
    }

    [Fact]
    public void Хост_не_может_покинуть_комнату()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);
        rooms.Join(room, Alice);

        Assert.Throws<InvalidOperationException>(() => rooms.Leave(room, Host));

        Assert.Equal(new[] { Host, Alice }, rooms.TryGetRoom(room)!.Participants);
    }

    [Fact]
    public void Хост_закрывает_комнату()
    {
        var rooms = NewRooms();
        var room = NewRoom(rooms);

        rooms.Close(room, Host);

        Assert.Equal(RoomStatus.Closed, rooms.TryGetRoom(room)!.Status);
    }
}
