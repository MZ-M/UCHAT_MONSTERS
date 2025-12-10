using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace uchat_client
{
    public class ConnectService
    {
        public static async Task<bool> Connect(bool loginOrRegister)
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

                // ----------------------------
                // AUTHENTICATION
                // ----------------------------
                if (loginOrRegister)
                {
                    Console.Write("[L]ogin or [R]egister: ");
                    string? mode = Console.ReadLine()?.Trim().ToUpper();
                    string authMode = (mode == "R") ? "REGISTER" : "LOGIN";

                    Console.Write("Username: ");
                    Program.savedLogin = Console.ReadLine()!;

                    Console.Write("Password: ");
                    Program.savedPass = Console.ReadLine()!;

                    await Program.writer.WriteLineAsync(
                        $"AUTH|{authMode}|{Program.savedLogin}|{Program.savedPass}");
                }
                else
                {
                    // reconnect login
                    await Program.writer.WriteLineAsync(
                        $"AUTH|LOGIN|{Program.savedLogin}|{Program.savedPass}");
                }

                string? resp = await Program.reader.ReadLineAsync();

                if (resp != "AUTH|OK")
                {
                    Console.WriteLine(resp);
                    return false;
                }

                Console.WriteLine("[AUTH OK]");

                Program.isConnected = true;

                // ------------------------------------------------------
                // START RECEIVING LOOP (NEW RECEIVER CLASS)
                // ------------------------------------------------------
                Receiver.Start();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CONNECT ERROR] " + ex.Message);
                return false;
            }
        }
    }

    // ================================================================
    // RECONNECT LOGIC
    // ================================================================
    class Reconnect
    {
        public static async void StartReconnectLoop()
        {
            while (!Program.isConnected)
            {
                try
                {
                    Console.WriteLine("[RECONNECTING...]");

                    if (await ConnectService.Connect(loginOrRegister: false))
                    {
                        Console.WriteLine("[RECONNECTED]");
                        return;
                    }
                }
                catch
                {
                    // ignore
                }

                await Task.Delay(3000);
            }
        }
    }
}
