using System;
using System.Threading.Tasks;

namespace uchat_client
{
    class Receiver
    {
        public static void Start()
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

                        MessageHandler.ProcessMessage(msg);
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
}