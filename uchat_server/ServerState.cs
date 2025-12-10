using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uchat_server
{
    public static class ServerState
    {
        // Глобальный список подключённых клиентов
        public static List<ClientSession> clients { get; } = new();

        public static void Broadcast(string message)
        {
            lock (clients)
            {
                foreach (var c in clients.Where(c => c.IsAuthenticated))
                {
                    try { _ = c.Send(message); }
                    catch { }
                }
            }
        }

        public static void BroadcastUsers()
        {
            lock (clients)
            {
                var list = string.Join(",",
                    clients
                        .Where(c => c.IsAuthenticated && !string.IsNullOrWhiteSpace(c.Username))
                        .Select(c => c.Username));

                foreach (var c in clients.Where(c => c.IsAuthenticated))
                {
                    try { _ = c.Send("USERS|" + list); }
                    catch { }
                }
            }
        }
    }
}
