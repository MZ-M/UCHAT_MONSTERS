using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uchat_server
{
    public static class ChatServer
    {
        public static async Task HandleClient(ClientSession session)
        {
            try
            {
                while (true)
                {
                    var cmd = await session.ReadLine();
                    if (cmd == null)
                        break;

                    Console.WriteLine($"RAW from {session.Username ?? "?"}: {cmd}");

                    await CommandProcessor.ProcessAsync(session, cmd);
                }
            }
            catch { }

            lock (ServerState.clients)
                ServerState.clients.Remove(session);

            ServerState.BroadcastUsers();
            Console.WriteLine($"Client disconnected: {session.Username}");
        }
    }
}
