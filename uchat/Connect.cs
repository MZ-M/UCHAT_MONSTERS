using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Net.Sockets;
using System.Text;

namespace Client
{
    public class Connect_serviese
    {
        public static async Task<bool> Connect(bool logReg)
        {
            try
            {
                Program.client = new TcpClient();
                await Program.client.ConnectAsync(Program.serverIp, Program.port);

                var stream = Program.client.GetStream();
                Program.reader = new StreamReader(stream, new UTF8Encoding(false));
                Program.writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                Console.WriteLine("[CONNECTED]");

                if (logReg)
                {
                    Console.Write("[L]ogin or [R]egister: ");
                    var mode = Console.ReadLine()?.Trim().ToUpper();
                    string authMode = mode == "R" ? "REGISTER" : "LOGIN";

                    Console.Write("Username: ");
                    Program.savedLogin = Console.ReadLine()!;

                    Console.Write("Password: ");
                    Program.savedPass = Console.ReadLine()!;

                    await Program.writer.WriteLineAsync(
                        $"AUTH|{authMode}|{Program.savedLogin}|{Program.savedPass}");
                }
                else
                {
                    await Program.writer.WriteLineAsync(
                        $"AUTH|LOGIN|{Program.savedLogin}|{Program.savedPass}");
                }

                var resp = await Program.reader.ReadLineAsync();

                if (resp != "AUTH|OK")
                {
                    Console.WriteLine("[AUTH FAILED]");
                    return false;
                }

                Console.WriteLine("[AUTH OK]");

                Program.isConnected = true;
                Receive.StartReceiveLoop();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CONNECT ERROR] " + ex.Message);
                return false;
            }
        }
    }

    class Receive
    {
        public static void StartReceiveLoop()
        {
            _ = Task.Run(async () =>
            {
                while (Program.isConnected)
                {
                    try
                    {
                        var msg = await Program.reader.ReadLineAsync();
                        if (msg == null)
                            throw new Exception("Disconnected");

                        Massage.ProcessMessage(msg);
                    }
                    catch
                    {
                        Program.isConnected = false;
                        Console.WriteLine("\n[SERVER LOST]");
                        Reconnect.StartReconnectLoop();
                        break;
                    }
                }
            });
        }
    }

    class Reconnect
    {
        public static async void StartReconnectLoop()
        {
            while (!Program.isConnected)
            {
                try
                {
                    Console.WriteLine("[RECONNECTING...]");

                    if (await Connect_serviese.Connect(logReg: false))
                    {
                        Console.WriteLine("[RECONNECTED]");
                        return;
                    }
                }
                catch
                {

                }

                await Task.Delay(3000);
            }
        }
    }
}