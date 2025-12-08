using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static TcpClient client;
    static StreamReader reader;
    static StreamWriter writer;
    // current chat mode: "all", "pm:<user>", "room:<room>"
    static string chatMode = "all";

    static readonly object writerLock = new();

    static bool isConnected = false;

    static string serverIp = "";
    static int serverPort = 0;

    static string savedLogin = "";
    static string savedPass = "";

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== uChat Console Client ===");

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: uchat <server_ip> <port>");
            return;
        }

        var cleanArgs = args.Where(a => a != "--").ToArray();

        if (cleanArgs.Length != 2)
        {
            Console.WriteLine("Usage: uchat <server_ip> <port>");
            return;
        }

        serverIp = cleanArgs[0];

        if (!int.TryParse(cleanArgs[1], out serverPort))
        {
            Console.WriteLine("Invalid port.");
            return;
        }

        if (!await Connect(firstLogin: true))
            return;

        StartSendLoop();
    }

    // ---------------------------------------------
    // CONNECT
    // ---------------------------------------------
    static async Task<bool> Connect(bool firstLogin)
    {
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(serverIp, serverPort);

            var stream = client.GetStream();

            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            Console.WriteLine("[CONNECTED]");

            if (firstLogin)
            {
                Console.Write("[L]ogin or [R]egister: ");
                string? mode = Console.ReadLine()?.Trim().ToUpper();
                string authMode = (mode == "R") ? "REGISTER" : "LOGIN";

                Console.Write("Username: ");
                savedLogin = Console.ReadLine() ?? "";

                Console.Write("Password: ");
                savedPass = Console.ReadLine() ?? "";

                lock (writerLock)
                {
                    writer.WriteLine($"AUTH|{authMode}|{savedLogin}|{savedPass}");
                    writer.Flush();
                }
            }
            else
            {
                lock (writerLock)
                {
                    writer.WriteLine($"AUTH|LOGIN|{savedLogin}|{savedPass}");
                    writer.Flush();
                }
            }

            string? resp = await reader.ReadLineAsync();

            if (resp == null)
            {
                Console.WriteLine("[AUTH ERROR: no response]");
                return false;
            }

            if (resp.StartsWith("AUTH|FAIL"))
            {
                var parts = resp.Split('|');
                Console.WriteLine("[AUTH FAILED] " + (parts.Length > 2 ? parts[2] : ""));
                return false;
            }

            if (resp != "AUTH|OK")
            {
                Console.WriteLine("[AUTH ERROR] Unexpected: " + resp);
                return false;
            }

            Console.WriteLine("[AUTH OK]");

            isConnected = true;
            StartReceiveLoop();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[CONNECT ERROR] " + ex.Message);
            return false;
        }
    }

    // ---------------------------------------------
    // RECEIVE LOOP
    // ---------------------------------------------
    static void StartReceiveLoop()
    {
        _ = Task.Run(async () =>
        {
            while (isConnected)
            {
                try
                {
                    var msg = await reader.ReadLineAsync();
                    if (msg == null) throw new Exception("Disconnected");

                    ProcessMessage(msg);
                }
                catch
                {
                    isConnected = false;
                    Console.WriteLine("\n[SERVER LOST]");
                    StartReconnectLoop();
                    break;
                }
            }
        });
    }

    // ---------------------------------------------
    // RECONNECT LOOP
    // ---------------------------------------------
    static async void StartReconnectLoop()
    {
        while (!isConnected)
        {
            try
            {
                Console.WriteLine("[RECONNECTING...]");

                if (await Connect(firstLogin: false))
                {
                    Console.WriteLine("[RECONNECTED]");
                    return;
                }
            }
            catch { }

            await Task.Delay(3000);
        }
    }

    // ---------------------------------------------
    // SEND LOOP
    // ---------------------------------------------
    static async void StartSendLoop()
    {
        ShowHelp();

        chatMode = "all";
        Console.WriteLine("=== PUBLIC CHAT ===");
        RequestHistory();

        while (true)
        {
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (!isConnected)
            {
                Console.WriteLine("[OFFLINE]");
                continue;
            }

            if (IsHelp(input))
            {
                ShowHelp();
                continue;
            }

            // переключение чатов
            if (input.StartsWith("/chat ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 3);

                if (p.Length < 2)
                {
                    Console.WriteLine("Usage:");
                    Console.WriteLine(" /chat public");
                    Console.WriteLine(" /chat pm <user>");
                    Console.WriteLine(" /chat room <room>");
                    continue;
                }

                if (p[1].Equals("public", StringComparison.OrdinalIgnoreCase))
                {
                    chatMode = "all";
                    Console.Clear();
                    Console.WriteLine("=== PUBLIC CHAT ===");
                    RequestHistory();
                    continue;
                }

                if (p[1].Equals("pm", StringComparison.OrdinalIgnoreCase))
                {
                    if (p.Length < 3)
                    {
                        Console.WriteLine("Usage: /chat pm <user>");
                        continue;
                    }

                    chatMode = "pm:" + p[2];
                    Console.Clear();
                    Console.WriteLine($"=== PRIVATE CHAT WITH {p[2]} ===");
                    RequestHistory();
                    continue;
                }

                if (p[1].Equals("room", StringComparison.OrdinalIgnoreCase))
                {
                    if (p.Length < 3)
                    {
                        Console.WriteLine("Usage: /chat room <room>");
                        continue;
                    }

                    chatMode = "room:" + p[2];
                    Console.Clear();
                    Console.WriteLine($"=== ROOM CHAT: {p[2]} ===");
                    RequestHistory();
                    continue;
                }

                Console.WriteLine("Unknown chat mode");
                continue;
            }

            // pm одноразовый
            if (input.StartsWith("/pm ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 3);
                if (p.Length < 3)
                {
                    Console.WriteLine("Usage: /pm <user> <message>");
                    continue;
                }

                lock (writerLock)
                {
                    writer.WriteLine($"MSG|{p[1]}|{p[2]}");
                    writer.Flush();
                }
                continue;
            }

            // файлы
            if (input.StartsWith("/file ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 3);
                if (p.Length < 3)
                {
                    Console.WriteLine("Usage: /file <user|all> <path>");
                    continue;
                }

                string file = p[2];

                if (!File.Exists(file))
                {
                    Console.WriteLine("File not found.");
                    continue;
                }

                string fn = Path.GetFileName(file);
                long size = new FileInfo(file).Length;

                await Task.Run(() =>
                {
                    lock (writerLock)
                    {
                        writer.WriteLine($"FILE|{p[1]}|{fn}|{size}");
                        writer.Flush();

                        using var fs = File.OpenRead(file);
                        fs.CopyTo(writer.BaseStream);
                        writer.BaseStream.Flush();
                    }
                });

                Console.WriteLine($"[FILE SENT] {fn}");
                continue;
            }

            // редактирование
            if (input.StartsWith("/edit ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 3);
                if (p.Length < 3)
                {
                    Console.WriteLine("Usage: /edit <id> <text>");
                    continue;
                }

                lock (writerLock)
                {
                    writer.WriteLine($"EDIT|{p[1]}|{p[2]}");
                    writer.Flush();
                }
                continue;
            }

            // удаление
            if (input.StartsWith("/del ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 2);

                if (p.Length < 2)
                {
                    Console.WriteLine("Usage: /del <id>");
                    continue;
                }

                lock (writerLock)
                {
                    writer.WriteLine($"DEL|{p[1]}");
                    writer.Flush();
                }

                continue;
            }

            // reload history
            if (input.Equals("/lh", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                RequestHistory();
                continue;
            }

            if (input.Equals("/cl", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                continue;
            }

            // create room
            if (input.StartsWith("/create ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 2);
                if (p.Length < 2)
                {
                    Console.WriteLine("Usage: /create <room>");
                    continue;
                }

                lock (writerLock)
                {
                    writer.WriteLine($"ROOM_CREATE|{p[1]}");
                    writer.Flush();
                }
                continue;
            }

            // invite
            if (input.StartsWith("/invite ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 3);
                if (p.Length < 3)
                {
                    Console.WriteLine("Usage: /invite <user> <room>");
                    continue;
                }

                string user = p[1];
                string room = p[2];

                lock (writerLock)
                {
                    writer.WriteLine($"ROOM_INVITE|{room}|{user}");
                    writer.Flush();
                }
                continue;
            }

            // join room
            if (input.StartsWith("/join ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 2);
                if (p.Length < 2)
                {
                    Console.WriteLine("Usage: /join <room>");
                    continue;
                }

                lock (writerLock)
                {
                    writer.WriteLine($"ROOM_JOIN|{p[1]}");
                    writer.Flush();
                }
                continue;
            }

            // leave room
            if (input.StartsWith("/leave ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 2);
                if (p.Length < 2)
                {
                    Console.WriteLine("Usage: /leave <room>");
                    continue;
                }

                lock (writerLock)
                {
                    writer.WriteLine($"ROOM_LEAVE|{p[1]}");
                    writer.Flush();
                }
                continue;
            }

            // send to room
            if (input.StartsWith("/room ", StringComparison.OrdinalIgnoreCase))
            {
                var p = input.Split(' ', 3);
                if (p.Length < 3)
                {
                    Console.WriteLine("Usage: /room <room> <text>");
                    continue;
                }

                string room = p[1];
                string text = p[2];

                lock (writerLock)
                {
                    writer.WriteLine($"ROOM_MSG|{room}|{text}");
                    writer.Flush();
                }
                continue;
            }

            // обычное сообщение в текущий чат
            lock (writerLock)
            {
                if (chatMode == "all")
                {
                    writer.WriteLine($"MSG|all|{input}");
                }
                else if (chatMode.StartsWith("pm:"))
                {
                    string user = chatMode.Substring(3);
                    writer.WriteLine($"MSG|{user}|{input}");
                }
                else if (chatMode.StartsWith("room:"))
                {
                    string room = chatMode.Substring(5);
                    writer.WriteLine($"ROOM_MSG|{room}|{input}");
                }

                writer.Flush();
            }
        }
    }

    // ---------------------------------------------
    // MESSAGE HANDLER
    // ---------------------------------------------
    static void ProcessMessage(string msg)
    {
        try
        {
            if (msg == "PING")
            {
                lock (writerLock)
                {
                    writer.WriteLine("PONG");
                    writer.Flush();
                }
                return;
            }

            if (msg == "HISTORY_UPDATED")
            {
                // сервер говорит "история изменилась" — перезагружаем только текущий чат
                Console.Clear();
                RequestHistory();
                return;
            }

            if (msg.StartsWith("USERS|"))
            {
                Console.WriteLine("[ONLINE] " + msg.Substring(6));
                return;
            }

            if (msg.StartsWith("FILE|"))
            {
                string[] p = msg.Split('|');

                string sender = p[1];
                string file = p[2];
                long size = long.Parse(p[3]);

                Console.WriteLine($"[FILE] {sender} → {file} ({size} bytes)");

                Directory.CreateDirectory("downloads");
                string savePath = Path.Combine("downloads", file);

                using FileStream fs = new(savePath, FileMode.Create);
                byte[] buf = new byte[8192];
                long remain = size;

                while (remain > 0)
                {
                    int read = reader.BaseStream.Read(buf, 0, (int)Math.Min(buf.Length, remain));
                    if (read <= 0) break;

                    fs.Write(buf, 0, read);
                    remain -= read;
                }

                string? end = reader.ReadLine();
                if (end != "FILE_END")
                    Console.WriteLine("[ERROR] Expected FILE_END");

                Console.WriteLine($"[SAVED] {savePath}");
                return;
            }

            if (msg.StartsWith("ROOM|"))
            {
                var p = msg.Split('|');

                switch (p[1])
                {
                    case "CREATED":
                        Console.WriteLine($"[ROOM] Created: {p[2]}");
                        break;

                    case "EXISTS":
                        Console.WriteLine($"[ROOM] Already exists: {p[2]}");
                        break;

                    case "INVITED":
                        Console.WriteLine($"[ROOM] You were invited to '{p[2]}' by {p[3]}");
                        break;

                    case "INVITE_OK":
                        Console.WriteLine($"[ROOM] User {p[3]} invited to '{p[2]}'");
                        break;

                    case "JOINED":
                        Console.WriteLine($"[ROOM] Joined room: {p[2]}");
                        break;

                    case "LEFT":
                        Console.WriteLine($"[ROOM] Left room: {p[2]}");
                        break;

                    default:
                        Console.WriteLine("[ROOM] " + msg);
                        break;
                }

                return;
            }

            if (msg.StartsWith("MSG|"))
            {
                var p = msg.Split('|', 6);

                long id = long.Parse(p[1]);
                string time = p[2];
                string from = p[3];
                string to = p[4];
                string text = p[5];

                // фильтруем по текущему чату

                if (chatMode == "all")
                {
                    if (!to.Equals("all", StringComparison.OrdinalIgnoreCase))
                        return;
                }
                else if (chatMode.StartsWith("pm:"))
                {
                    string user = chatMode.Substring(3);
                    bool isPM =
                        (from == user && to == savedLogin) ||
                        (to == user && from == savedLogin);

                    if (!isPM)
                        return;
                }
                else if (chatMode.StartsWith("room:"))
                {
                    string room = chatMode.Substring(5);
                    if (to != room)
                        return;
                }

                Console.WriteLine($"[{id}] {time} | {from} → {to}: {text}");
                return;
            }

            if (msg.StartsWith("ERROR|"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] " + msg.Substring(6));
                Console.ResetColor();
                return;
            }

            Console.WriteLine(msg);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[CLIENT ERROR] " + ex.Message);
        }
    }

    static void RequestHistory()
    {
        lock (writerLock)
        {
            if (chatMode == "all")
            {
                writer.WriteLine("HISTORY|PUBLIC");
            }
            else if (chatMode.StartsWith("pm:"))
            {
                string user = chatMode.Substring(3);
                writer.WriteLine($"HISTORY|PM|{user}");
            }
            else if (chatMode.StartsWith("room:"))
            {
                string room = chatMode.Substring(5);
                writer.WriteLine($"HISTORY|ROOM|{room}");
            }

            writer.Flush();
        }
    }

    // ---------------------------------------------
    // HELP
    // ---------------------------------------------
    static bool IsHelp(string input)
        => input.Equals("/h", StringComparison.OrdinalIgnoreCase)
        || input.Equals("/help", StringComparison.OrdinalIgnoreCase);

    static void ShowHelp()
    {
        Console.WriteLine("\n=== uChat HELP ===");
        Console.WriteLine(" <text>              - send message to current chat");
        Console.WriteLine(" /pm user msg        - one-time private message");
        Console.WriteLine(" /file user|all path - send file");
        Console.WriteLine(" /edit id msg        - edit message");
        Console.WriteLine(" /del id             - delete message");
        Console.WriteLine(" /lh                 - reload history of current chat");
        Console.WriteLine(" /cl                 - clear screen");
        Console.WriteLine(" /help               - help");

        Console.WriteLine("\nCHAT MODES:");
        Console.WriteLine(" /chat public        - switch to public chat");
        Console.WriteLine(" /chat pm user       - switch to dialog with user");
        Console.WriteLine(" /chat room room     - switch to room chat");

        Console.WriteLine("\nROOM COMMANDS:");
        Console.WriteLine(" /create room        - create room");
        Console.WriteLine(" /invite user room   - invite user to room");
        Console.WriteLine(" /join room          - join invited room");
        Console.WriteLine(" /leave room         - leave room");
        Console.WriteLine(" /room room text     - send message to room");
        Console.WriteLine();
    }
}
