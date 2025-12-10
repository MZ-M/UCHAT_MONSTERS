using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace uchat_client
{
    class Program
    {
        // --------------------------------------------------------------
        // GLOBAL STATE 
        // --------------------------------------------------------------
        public static TcpClient client = null!;
        public static StreamReader reader = null!;
        public static StreamWriter writer = null!;
        public static readonly object writerLock = new();

        public static bool isConnected = false;

        public static string serverIp = "";
        public static int port = 0;

        // current chat mode: "all", "pm:user", "room:name"
        public static string chatMode = "all";

        // cached auth credentials (used for reconnect)
        public static string savedLogin = "";
        public static string savedPass = "";

        // --------------------------------------------------------------
        // MAIN ENTRY
        // --------------------------------------------------------------
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== uChat Console Client ===");

            // --------------------------------------
            // ARGUMENTS
            // --------------------------------------
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: uchat_client <server_ip> <port>");
                return;
            }

            serverIp = args[0];

            if (!int.TryParse(args[1], out port))
            {
                Console.WriteLine("Invalid port.");
                return;
            }

            // --------------------------------------
            // CONNECT + AUTH
            // --------------------------------------
            if (!await ConnectService.Connect(loginOrRegister: true))
            {
                return;
            }

            // --------------------------------------
            // START SEND LOOP
            // --------------------------------------
            SendLoop.StartSendLoop();
        }
    }

    // --------------------------------------------------------------
    // HELP SYSTEM
    // --------------------------------------------------------------
    class Help
    {
        public static bool IsHelp(string input)
            => input.Equals("/h", StringComparison.OrdinalIgnoreCase)
            || input.Equals("/help", StringComparison.OrdinalIgnoreCase);

        public static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("=== uChat HELP ===");

            // --------------------------------------------------------------
            // GENERAL CHAT
            // --------------------------------------------------------------
            Console.WriteLine("\nGENERAL CHAT:");
            Console.WriteLine("  <text>                  send message to public chat");
            Console.WriteLine("  /all                    switch to public chat mode");

            // --------------------------------------------------------------
            // PRIVATE MESSAGES
            // --------------------------------------------------------------
            Console.WriteLine("\nPRIVATE MESSAGES:");
            Console.WriteLine("  /pm <user>              switch to PM mode with <user>");
            Console.WriteLine("  /pm <user> <text>       send PM directly");

            // --------------------------------------------------------------
            // ROOMS
            // --------------------------------------------------------------
            Console.WriteLine("\nROOMS:");
            Console.WriteLine("  /room <name>            switch to room mode (no join)");
            Console.WriteLine("  /roomcreate <name>      create room");
            Console.WriteLine("  /roomdelete <name>      delete room");
            Console.WriteLine("  /roomjoin <name>        join room");
            Console.WriteLine("  /roomleave <name>       leave room");
            Console.WriteLine("  /roommsg <n> <text>     send message to room");
            Console.WriteLine("  /rooms, /roomlist       list all rooms");
            Console.WriteLine("  /roominfo <name>        show room info");
            Console.WriteLine("  /roomwho <name>         list users in room");
            Console.WriteLine("  /roomkick <r> <user>    kick user from room");
            Console.WriteLine("  /roomrename <o> <n>     rename a room");

            // --------------------------------------------------------------
            // FILE TRANSFER
            // --------------------------------------------------------------
            Console.WriteLine("\nFILES:");
            Console.WriteLine("  /file <target> <path>   send file to user or room");

            // --------------------------------------------------------------
            // EDIT / DELETE
            // --------------------------------------------------------------
            Console.WriteLine("\nEDIT / DELETE:");
            Console.WriteLine("  /edit <id> <text>       edit message");
            Console.WriteLine("  /del <id>               delete message");

            // --------------------------------------------------------------
            // HISTORY
            // --------------------------------------------------------------
            Console.WriteLine("\nHISTORY:");
            Console.WriteLine("  /lh                     reload history for current mode");

            // --------------------------------------------------------------
            // OTHER / UTILITIES
            // --------------------------------------------------------------
            Console.WriteLine("\nOTHER:");
            Console.WriteLine("  /cl                     clear screen");
            Console.WriteLine("  /h, /help               show this help");

            Console.WriteLine();
        }
    }
}