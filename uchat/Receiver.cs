using System;
using System.Text;
using System.Threading.Tasks;

namespace uchat_client
{
    class Receiver
    {
        public static void Start()
        {
            _ = Task.Run(() =>
            {
                while (Program.isConnected)
                {
                    try
                    {
                        var frame = FrameIO.ReadFrame(Program.reader);
                        if (frame == null)
                            throw new Exception("Disconnected");

                        if (frame.Type == FrameType.Text)
                        {
                            string msg = Encoding.UTF8.GetString(frame.Payload);
                            MessageHandler.ProcessMessage(msg);
                        }
                        else if (frame.Type == FrameType.FileChunk)
                        {
                            FileReceiver.ProcessChunk(frame.Payload);
                        }
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