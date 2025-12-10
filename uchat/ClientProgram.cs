using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    class Program
    {
        public static TcpClient client = null!;
        public static StreamReader reader = null!;
        public static StreamWriter writer = null!;

        public static bool isConnected;

        public static string serverIp = "";
        public static int port = 0;

        public static string savedLogin = "";
        public static string savedPass = "";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== uChat Console Client ===");

            if (args.Length != 2)
            {
                Console.WriteLine("Usage: uchat <server_ip> <port>");
                return;
            }

            serverIp = args[0];
            if (!int.TryParse(args[1], out port))
            {
                Console.WriteLine("Invalid port.");
                return;
            }

            if (!await Connect_serviese.Connect(logReg: true))
                return;

            SendMessage.StartSendLoop();
        }
    }

    class Help{
        public static bool IsHelp(string input)
            => input.Equals("/h", StringComparison.OrdinalIgnoreCase)
            || input.Equals("/help", StringComparison.OrdinalIgnoreCase);

        public static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("=== uChat HELP ===");
            Console.WriteLine("Public message:");
            Console.WriteLine("  <text>");
            Console.WriteLine();
            Console.WriteLine("Private:");
            Console.WriteLine("  /pm <user> <message>");
            Console.WriteLine();
            Console.WriteLine("File:");
            Console.WriteLine("  /file <user|all> <path>");
            Console.WriteLine();
            Console.WriteLine("Edit:");
            Console.WriteLine("  /edit <id> <new text>");
            Console.WriteLine();
            Console.WriteLine("Delete:");
            Console.WriteLine("  /del <id>");
            Console.WriteLine();
            Console.WriteLine("Reload history:");
            Console.WriteLine("  /lh");
            Console.WriteLine();
            Console.WriteLine("Clear console:");
            Console.WriteLine("  /cl");
            Console.WriteLine();
            Console.WriteLine("Help:");
            Console.WriteLine("  /h /help");
            Console.WriteLine();
        }
    }
}