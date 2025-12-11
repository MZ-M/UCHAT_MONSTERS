using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;

namespace uchat_server;

public class Program
{
    public static string TempDir = Path.Combine(AppContext.BaseDirectory, "Temp");

    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== uChat Server ===");

        // -------------------------------
        // 1. Validate args
        // -------------------------------
        if (args.Length != 1 || !int.TryParse(args[0], out int port))
        {
            Console.WriteLine("Usage: uchat_server <port>");
            Console.WriteLine("Example: uchat_server 8100");
            return;
        }

        Console.WriteLine($"[INFO] Starting on port {port}");
        Console.WriteLine($"[INFO] PID = {Environment.ProcessId}");
        Console.WriteLine();

        // -------------------------------
        // 2. Create temp folder
        // -------------------------------
        Directory.CreateDirectory(TempDir);
        Console.WriteLine($"[INIT] Temp folder: {TempDir}");

        // -------------------------------
        // 3. Initialize DB
        // -------------------------------
        Console.WriteLine("[INIT] Initializing database...");
        Database.Initialize();
        Console.WriteLine("[INIT] Database OK");

        // -------------------------------
        // 4. Load rooms
        // -------------------------------
        Console.WriteLine("[INIT] Loading rooms...");
        ServerState.LoadRoomsFromDatabase();
        Console.WriteLine("[INIT] Rooms loaded.");

        Console.WriteLine();

        // -------------------------------
        // 5. Start Listener
        // -------------------------------
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to start listener: {ex.Message}");
            return;
        }

        Console.WriteLine($"[OK] Server is running on port {port}");
        Console.WriteLine("[OK] Waiting for clients...");
        Console.WriteLine();

        // -------------------------------
        // 6. Accept clients
        // -------------------------------
        while (true)
        {
            TcpClient tcpClient;

            try
            {
                tcpClient = await listener.AcceptTcpClientAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Listener stopped: {ex.Message}");
                break;
            }

            var session = new ClientSession(tcpClient);

            lock (ServerState.clients)
                ServerState.clients.Add(session);

            Console.WriteLine($"[CONNECT] Client connected");

            _ = Task.Run(() => ChatServer.HandleClient(session));
        }
    }
}