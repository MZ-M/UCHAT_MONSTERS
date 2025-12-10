using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace uchat_server;
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== uchat_server starting ===");

        if (args.Length != 1)
        {
            Console.WriteLine("Usage: uchat_server <port>");
            return;
        }

        int port = int.Parse(args[0]);
        Console.WriteLine($"Process ID: {Environment.ProcessId}");

        // 1. Инициализация базы
        Database.Initialize();

        // 2. Запуск TCP listener
        TcpListener listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Listening on port {port}...");

        // 3. Главный цикл сервера
        while (true)
        {
            // ОС пробуждает ожидающий Accept при новом подключении
            var tcpClient = await listener.AcceptTcpClientAsync();

            var session = new ClientSession(tcpClient);

            lock (ServerState.clients)
                ServerState.clients.Add(session);

            _ = Task.Run(() => ChatServer.HandleClient(session));
        }
    }
}
