using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace uchat_server
{
    public static class ServerState
    {
        public static List<ClientSession> clients { get; } = new();
        public static List<StoredFile> PendingFiles { get; } = new();
        public static Dictionary<string, HashSet<string>> Rooms { get; } =
            new(StringComparer.OrdinalIgnoreCase);

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
        public static void LoadRoomsFromDatabase()
        {
            ServerState.Rooms.Clear();

            var rooms = Database.GetAllRooms();

            foreach (var room in rooms)
            {
                ServerState.Rooms[room.Name] =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var members = Database.GetRoomMembers(room.Id);

                foreach (var username in members)
                {
                    ServerState.Rooms[room.Name].Add(username);
                }
            }

            Console.WriteLine($"[ROOMS] Loaded {rooms.Count} rooms from database.");
        }

    }
}
