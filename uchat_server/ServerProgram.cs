using Microsoft.Data.Sqlite;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

// комнаты: имя → структура комнаты
Dictionary<string, Room> rooms = new();
Console.WriteLine("=== uChat Server (Stable Build) ===");

// ---------------------------------------------------------
// ARGUMENTS
// ---------------------------------------------------------

if (args.Length != 1)
{
    Console.WriteLine("Usage: uchat_server <port>");
    return;
}

int port = int.Parse(args[0]);
Console.WriteLine($"Port = {port}");
Console.WriteLine($"PID  = {Environment.ProcessId}");

// ---------------------------------------------------------
// DATABASE
// ---------------------------------------------------------

const string dbFile = "uchat.db";

SqliteConnection OpenDb()
{
    var db = new SqliteConnection($"Data Source={dbFile}");
    db.Open();
    return db;
}

void InitDB()
{
    using var db = OpenDb();

    var cmd = db.CreateCommand();
    cmd.CommandText = @"
    CREATE TABLE IF NOT EXISTS Users
    (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Username TEXT UNIQUE,
        Password TEXT
    );

    CREATE TABLE IF NOT EXISTS Messages
    (
       Id          INTEGER PRIMARY KEY AUTOINCREMENT,
       MessageType TEXT,            -- 'PUBLIC', 'PM', 'ROOM'
       Sender      TEXT,
       Receiver    TEXT,            -- для PUBLIC/PM
       Room        TEXT,            -- для ROOM
       Text        TEXT,
       Time        TEXT
    );
    ";
    cmd.ExecuteNonQuery();
}

InitDB();

// ---------------------------------------------------------
// SERVER START
// ---------------------------------------------------------

TcpListener listener = new(IPAddress.Any, port);
listener.Start();

Console.WriteLine("Server started. Waiting for clients...");

List<ClientSession> clients = new();


// ---------------------------------------------------------
// BROADCAST TEXT MESSAGE
// ---------------------------------------------------------

void Broadcast(string msg)
{
    List<ClientSession> dead = new();

    List<ClientSession> snapshot;
    lock (clients)
        snapshot = clients.ToList();

    foreach (var c in snapshot)
    {
        if (!c.IsAuthenticated || c.ReceivingFile)
            continue;

        try { c.Send(msg); }
        catch { dead.Add(c); }
    }

    if (dead.Count > 0)
    {
        lock (clients)
            foreach (var d in dead)
                clients.Remove(d);

        BroadcastUsers();
    }
}

// ---------------------------------------------------------
// BROADCAST USERS LIST
// ---------------------------------------------------------

void BroadcastUsers()
{
    lock (clients)
    {
        string list = string.Join(",",
            clients.Where(c => c.IsAuthenticated)
                   .Select(c => c.Username));

        foreach (var c in clients.Where(c => c.IsAuthenticated))
        {
            try { c.Send("USERS|" + list); }
            catch { }
        }
    }
}

// ---------------------------------------------------------
// MAIN CLIENT LOOP
// ---------------------------------------------------------

async Task HandleClient(ClientSession session)
{
    try
    {
        while (true)
        {
            string? cmd = await session.ReadLine();
            if (cmd == null)
                break;

            Console.WriteLine($"[{session.Username ?? "?"}] {cmd}");

            await ProcessCommand(session, cmd);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Client error ({session.Username}): {ex.Message}");
    }
    finally
    {
        lock (clients)
            clients.Remove(session);

        session.Close();
        BroadcastUsers();
        Console.WriteLine($"Client disconnected: {session.Username}");
    }
}

// ---------------------------------------------------------
// COMMAND PROCESSOR
// ---------------------------------------------------------

async Task ProcessCommand(ClientSession s, string cmd)
{
    var p = cmd.Split('|');

    switch (p[0])
    {
        case "PONG":
            s.LastPong = DateTime.Now;
            return;

        case "AUTH":
            await HandleAuth(s, p);
            return;

        case "MSG":
            await HandleMessage(s, p);
            return;

        case "HISTORY":
            await SendHistory(s, p);
            return;

        case "FILE":
            await HandleFile(s, p);
            return;

        case "EDIT":
            await HandleEdit(s, p);
            return;

        case "DEL":
            await HandleDelete(s, p);
            return;

        case "ROOM_CREATE":
            await HandleRoomCreate(s, p);
            break;

        case "ROOM_INVITE":
            await HandleRoomInvite(s, p);
            break;

        case "ROOM_JOIN":
            await HandleRoomJoin(s, p);
            break;

        case "ROOM_LEAVE":
            await HandleRoomLeave(s, p);
            break;

        case "ROOM_MSG":
            await HandleRoomMessage(s, p);
            break;

        default:
            await s.Send("ERROR|Unknown command");
            return;
    }
}

// ---------------------------------------------------------
// AUTH
// ---------------------------------------------------------

async Task HandleAuth(ClientSession s, string[] p)
{
    if (p.Length < 4) return;

    string mode = p[1];
    string login = p[2];
    string pass = p[3];

    using var db = OpenDb();

    if (mode == "REGISTER")
    {
        var check = db.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Users WHERE Username=@u;";
        check.Parameters.AddWithValue("@u", login);

        if ((long)check.ExecuteScalar()! > 0)
        {
            await s.Send("AUTH|FAIL|User exists");
            return;
        }

        var ins = db.CreateCommand();
        ins.CommandText = "INSERT INTO Users(Username,Password) VALUES(@u,@p)";
        ins.Parameters.AddWithValue("@u", login);
        ins.Parameters.AddWithValue("@p", pass);
        ins.ExecuteNonQuery();

        s.Authenticate(login);
        await s.Send("AUTH|OK");

        BroadcastUsers();
        return;
    }

    if (mode == "LOGIN")
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Username=@u AND Password=@p;";
        cmd.Parameters.AddWithValue("@u", login);
        cmd.Parameters.AddWithValue("@p", pass);

        if ((long)cmd.ExecuteScalar()! == 0)
        {
            await s.Send("AUTH|FAIL|Invalid credentials");
            return;
        }

        s.Authenticate(login);
        await s.Send("AUTH|OK");
        BroadcastUsers();
        return;
    }
}

// ---------------------------------------------------------
// SAVE MESSAGE (универсальный)
// ---------------------------------------------------------

long SaveMessage(string messageType, string sender, string? receiver, string? room, string text)
{
    using var db = OpenDb();
    var cmd = db.CreateCommand();

    cmd.CommandText =
    @"INSERT INTO Messages(MessageType,Sender,Receiver,Room,Text,Time)
      VALUES(@mt,@s,@r,@room,@t,@time);
      SELECT last_insert_rowid();";

    cmd.Parameters.AddWithValue("@mt", messageType);
    cmd.Parameters.AddWithValue("@s", sender);
    cmd.Parameters.AddWithValue("@r", (object?)receiver ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@room", (object?)room ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@t", text);
    cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

    return (long)cmd.ExecuteScalar()!;
}

// ---------------------------------------------------------
// MESSAGE HANDLER (PUBLIC / PM)
// ---------------------------------------------------------

async Task HandleMessage(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated)
    {
        await s.Send("ERROR|Not authorized");
        return;
    }

    if (p.Length < 3)
    {
        await s.Send("ERROR|Format: MSG|receiver|text");
        return;
    }

    string receiver = p[1];
    string text = string.Join("|", p.Skip(2));

    string messageType;
    string? dbReceiver = null;
    string? dbRoom = null;

    if (receiver.Equals("all", StringComparison.OrdinalIgnoreCase))
    {
        messageType = "PUBLIC";
        dbReceiver = "all";
    }
    else
    {
        messageType = "PM";
        dbReceiver = receiver;
    }

    long id = SaveMessage(messageType, s.Username!, dbReceiver, dbRoom, text);
    string time = DateTime.Now.ToString("HH:mm:ss");

    string formatted = $"MSG|{id}|{time}|{s.Username}|{receiver}|{text}";

    if (messageType == "PUBLIC")
    {
        // всем
        Broadcast(formatted);
        return;
    }

    // private message
    ClientSession? target;
    lock (clients)
        target = clients.FirstOrDefault(c => c.Username == receiver);

    if (target == null)
    {
        await s.Send("ERROR|User not online");
        return;
    }

    await target.Send(formatted);
    await s.Send(formatted);
}

// ---------------------------------------------------------
// SEND HISTORY (по выбранному типу чата)
// ---------------------------------------------------------

async Task SendHistory(ClientSession s, string[] p)
{
    using var db = OpenDb();
    var cmd = db.CreateCommand();

    // HISTORY (если понадобится — "общая" история)
    if (p.Length == 1)
    {
        cmd.CommandText = @"
            SELECT Id,Sender,Receiver,Text,Time
            FROM Messages
            WHERE (MessageType='PUBLIC')
               OR (MessageType='PM' AND (Sender=@me OR Receiver=@me))
            ORDER BY Time;
        ";
        cmd.Parameters.AddWithValue("@me", s.Username);
    }
    // HISTORY|PUBLIC
    else if (p.Length == 2 && p[1] == "PUBLIC")
    {
        cmd.CommandText = @"
            SELECT Id,Sender,Receiver,Text,Time
            FROM Messages
            WHERE MessageType='PUBLIC'
            ORDER BY Time;
        ";
    }
    // HISTORY|PM|user
    else if (p.Length == 3 && p[1] == "PM")
    {
        cmd.CommandText = @"
            SELECT Id,Sender,Receiver,Text,Time
            FROM Messages
            WHERE MessageType='PM'
              AND (
                    (Sender=@me AND Receiver=@u)
                 OR (Sender=@u   AND Receiver=@me)
                  )
            ORDER BY Time;
        ";
        cmd.Parameters.AddWithValue("@me", s.Username);
        cmd.Parameters.AddWithValue("@u", p[2]);
    }
    // HISTORY|ROOM|room
    else if (p.Length == 3 && p[1] == "ROOM")
    {
        // ВАЖНО: подставляем Room как Receiver, чтобы клиенту было удобно
        cmd.CommandText = @"
            SELECT Id,Sender,Room as Receiver,Text,Time
            FROM Messages
            WHERE MessageType='ROOM'
              AND Room=@room
            ORDER BY Time;
        ";
        cmd.Parameters.AddWithValue("@room", p[2]);
    }
    else
    {
        await s.Send("ERROR|Invalid HISTORY format");
        return;
    }

    using var r = cmd.ExecuteReader();

    while (r.Read())
    {
        await s.Send(
            $"MSG|{r.GetInt64(0)}|{r.GetString(4)}|{r.GetString(1)}|{r.GetString(2)}|{r.GetString(3)}"
        );
    }

    await s.Send("--END--");
}


// ---------------------------------------------------------
// FILE TRANSFER
// ---------------------------------------------------------

async Task HandleFile(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated)
    {
        await s.Send("ERROR|Not authorized");
        return;
    }

    if (p.Length < 4)
    {
        await s.Send("ERROR|Format: FILE|user|filename|size");
        return;
    }

    string receiver = p[1];
    string filename = p[2];
    long size = long.Parse(p[3]);

    const long MAX = 200L * 1024 * 1024;
    if (size > MAX)
    {
        await s.Send("ERROR|File too large");
        s.Close();
        return;
    }

    ClientSession? target;
    lock (clients)
        target = clients.FirstOrDefault(c => c.Username == receiver);

    if (target == null)
    {
        await s.Send("ERROR|User not online");
        return;
    }

    target.ReceivingFile = true;

    await target.Send($"FILE|{s.Username}|{filename}|{size}");

    byte[] buf = new byte[8192];
    long remain = size;

    while (remain > 0)
    {
        int read = await s.RawStream.ReadAsync(buf, 0, (int)Math.Min(buf.Length, remain));
        if (read <= 0) break;

        await target.RawStream.WriteAsync(buf, 0, read);
        remain -= read;
    }

    await target.Send("FILE_END");
    target.ReceivingFile = false;

    Console.WriteLine($"FILE OK: {s.Username} → {receiver} : {filename}");
}

// ---------------------------------------------------------
// EDIT MESSAGE
// ---------------------------------------------------------

async Task HandleEdit(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated) return;
    if (p.Length < 3) return;

    long id = long.Parse(p[1]);
    string newText = string.Join("|", p.Skip(2)) + " (edited)";

    using var db = OpenDb();

    var cmd = db.CreateCommand();
    cmd.CommandText =
        "UPDATE Messages SET Text=@t WHERE Id=@id AND Sender=@s";

    cmd.Parameters.AddWithValue("@t", newText);
    cmd.Parameters.AddWithValue("@id", id);
    cmd.Parameters.AddWithValue("@s", s.Username);

    if (cmd.ExecuteNonQuery() == 0)
    {
        await s.Send("ERROR|Cannot edit");
        return;
    }

    // уведомляем клиентов, что история обновилась
    Broadcast("HISTORY_UPDATED");
}

// ---------------------------------------------------------
// DELETE MESSAGE
// ---------------------------------------------------------

async Task HandleDelete(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated) return;
    if (p.Length < 2) return;

    long id = long.Parse(p[1]);

    using var db = OpenDb();

    var cmd = db.CreateCommand();
    cmd.CommandText =
        "DELETE FROM Messages WHERE Id=@id AND Sender=@s";

    cmd.Parameters.AddWithValue("@id", id);
    cmd.Parameters.AddWithValue("@s", s.Username);

    if (cmd.ExecuteNonQuery() == 0)
    {
        await s.Send("ERROR|Cannot delete");
        return;
    }

    Broadcast("HISTORY_UPDATED");
}


// ================= ROOM: CREATE =================
async Task HandleRoomCreate(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated)
    {
        await s.Send("ERROR|Not authorized");
        return;
    }

    if (p.Length < 2)
    {
        await s.Send("ERROR|Room name required");
        return;
    }

    string room = p[1];
    bool exists;

    lock (rooms)
    {
        exists = rooms.ContainsKey(room);
        if (!exists)
        {
            var r = new Room { Name = room };
            r.Members.Add(s.Username!);
            rooms[room] = r;
        }
    }

    if (exists)
    {
        await s.Send($"ROOM|EXISTS|{room}");
        return;
    }

    await s.Send($"ROOM|CREATED|{room}");
}

// ================= ROOM: INVITE =================
async Task HandleRoomInvite(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated)
    {
        await s.Send("ERROR|Not authorized");
        return;
    }

    if (p.Length < 3)
    {
        await s.Send("ERROR|Usage: ROOM_INVITE|room|user");
        return;
    }

    string room = p[1];
    string targetUser = p[2];

    Room? r;
    lock (rooms)
        rooms.TryGetValue(room, out r);

    if (r == null)
    {
        await s.Send("ERROR|Room not found");
        return;
    }

    if (!r.Members.Contains(s.Username!))
    {
        await s.Send("ERROR|You are not in this room");
        return;
    }

    ClientSession? target;
    lock (clients)
        target = clients.FirstOrDefault(c => c.IsAuthenticated && c.Username == targetUser);

    if (target == null)
    {
        await s.Send("ERROR|User not online");
        return;
    }

    lock (rooms)
        r.Members.Add(targetUser);

    await target.Send($"ROOM|INVITED|{room}|{s.Username}");
    await s.Send($"ROOM|INVITE_OK|{room}|{targetUser}");
}

// ================= ROOM: JOIN =================
async Task HandleRoomJoin(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated)
    {
        await s.Send("ERROR|Not authorized");
        return;
    }

    if (p.Length < 2)
    {
        await s.Send("ERROR|Usage: ROOM_JOIN|room");
        return;
    }

    string room = p[1];

    Room? r;
    lock (rooms)
        rooms.TryGetValue(room, out r);

    if (r == null)
    {
        await s.Send("ERROR|Room not found");
        return;
    }

    if (!r.Members.Contains(s.Username!))
    {
        await s.Send("ERROR|You are not invited to this room");
        return;
    }

    await s.Send($"ROOM|JOINED|{room}");
}

// ================= ROOM: LEAVE =================
async Task HandleRoomLeave(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated)
    {
        await s.Send("ERROR|Not authorized");
        return;
    }

    if (p.Length < 2)
    {
        await s.Send("ERROR|Usage: ROOM_LEAVE|room");
        return;
    }

    string room = p[1];

    Room? r;
    lock (rooms)
        rooms.TryGetValue(room, out r);

    if (r == null)
    {
        await s.Send("ERROR|Room not found");
        return;
    }

    bool removed = false;
    lock (rooms)
        removed = r.Members.Remove(s.Username!);

    if (!removed)
    {
        await s.Send("ERROR|You are not in this room");
        return;
    }

    await s.Send($"ROOM|LEFT|{room}");
}

// ================= ROOM: MESSAGE =================
async Task HandleRoomMessage(ClientSession s, string[] p)
{
    if (!s.IsAuthenticated)
    {
        await s.Send("ERROR|Not authorized");
        return;
    }

    if (p.Length < 3)
    {
        await s.Send("ERROR|Format: ROOM_MSG|room|text");
        return;
    }

    string room = p[1];
    string text = string.Join("|", p, 2, p.Length - 2);

    Room? r;

    lock (rooms)
    {
        rooms.TryGetValue(room, out r);
    }

    if (r == null)
    {
        await s.Send($"ERROR|Room '{room}' does not exist");
        return;
    }

    bool isMember;
    lock (rooms)
        isMember = r.Members.Contains(s.Username!);

    if (!isMember)
    {
        await s.Send("ERROR|You are not a member of this room");
        return;
    }

    long id = SaveMessage("ROOM", s.Username!, null, room, text);
    string time = DateTime.Now.ToString("HH:mm:ss");

    string formatted = $"MSG|{id}|{time}|{s.Username}|{room}|{text}";

    List<ClientSession> targets;
    lock (clients)
    {
        targets = clients
            .Where(c => c.IsAuthenticated && r.Members.Contains(c.Username!))
            .ToList();
    }

    foreach (var t in targets)
        _ = t.Send(formatted);
}

// ---------------------------------------------------------
// ACCEPT LOOP
// ---------------------------------------------------------

while (true)
{
    var tcp = await listener.AcceptTcpClientAsync();
    var session = new ClientSession(tcp);

    lock (clients)
        clients.Add(session);

    _ = Task.Run(() => HandleClient(session));
}

// ================= ROOMS =================
class Room
{
    public string Name { get; set; } = "";
    public HashSet<string> Members { get; } = new();
}

// ======================================================================
//                         CLIENT SESSION CLASS
// ======================================================================

class ClientSession
{
    private readonly TcpClient client;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly object sendLock = new();

    public string? Username { get; private set; }
    public bool IsAuthenticated => Username != null;
    public bool ReceivingFile = false;
    public DateTime LastPong = DateTime.Now;

    public NetworkStream RawStream => (NetworkStream)reader.BaseStream;

    public ClientSession(TcpClient c)
    {
        client = c;
        var stream = client.GetStream();

        reader = new StreamReader(stream, new UTF8Encoding(false));
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Authenticate(string user) => Username = user;

    public Task<string?> ReadLine() => reader.ReadLineAsync();

    public Task Send(string msg)
    {
        lock (sendLock)
        {
            writer.WriteLine(msg);
        }
        return Task.CompletedTask;
    }

    public void Close()
    {
        try { client.Close(); } catch { }
    }
}
