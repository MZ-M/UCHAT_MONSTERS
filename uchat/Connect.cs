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
                Program.reader = new BinaryReader(stream, Encoding.UTF8);
                Program.writer = new BinaryWriter(stream, Encoding.UTF8);

                Console.WriteLine("[CONNECTED]");

                // ----------------------------
                // AUTHENTICATION
                // ----------------------------
                string authMode;

                if (loginOrRegister)
                {
                    Console.Write("[L]ogin or [R]egister: ");
                    string? mode = Console.ReadLine()?.Trim().ToUpper();
                    authMode = (mode == "R") ? "REGISTER" : "LOGIN";

                    Console.Write("Username: ");
                    Program.savedLogin = Console.ReadLine()!;

                    Console.Write("Password: ");
                    Program.savedPass = Console.ReadLine()!;
                }
                else
                {
                    authMode = "LOGIN";
                }

                FrameIO.SendText(
                    Program.writer,
                    $"AUTH|{authMode}|{Program.savedLogin}|{Program.savedPass}"
                );

                Frame? respFrame = FrameIO.ReadFrame(Program.reader);
                if (respFrame == null || respFrame.Type != FrameType.Text)
                {
                    Console.WriteLine("[AUTH FAILED] no response");
                    return false;
                }

                string resp = Encoding.UTF8.GetString(respFrame.Payload);

                if (resp != "AUTH|OK")
                {
                    Console.WriteLine("[AUTH FAILED] " + resp);
                    return false;
                }

                Console.WriteLine("[AUTH OK]");

                Program.isConnected = true;

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
