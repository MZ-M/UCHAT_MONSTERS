using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace uchat_server
{
    public static class CommandProcessor
    {
        public static async Task ProcessAsync(ClientSession s, string cmd)
        {
            var parts = cmd.Split('|');

            switch (parts[0])
            {
                case "AUTH":
                    await HandleAuth(s, parts);
                    break;

                case "MSG":
                    await HandleMessage(s, parts);
                    break;

                case "HISTORY":
                    if (s.IsAuthenticated)
                        await SendHistory(s, parts);
                    break;

                case "FILE":
                    await HandleFile(s, parts);
                    break;

                case "EDIT":
                    await HandleEdit(s, parts);
                    break;

                case "DEL":
                    await HandleDelete(s, parts);
                    break;

                case "ROOM_CREATE":
                    await HandleRoomCreate(s, parts);
                    break;

                case "ROOM_DELETE":
                    await HandleRoomDelete(s, parts);
                    break;

                case "ROOM_JOIN":
                    await HandleRoomJoin(s, parts);
                    break;

                case "ROOM_LEAVE":
                    await HandleRoomLeave(s, parts);
                    break;

                case "ROOM_MSG":
                    await HandleRoomMessage(s, parts);
                    break;

                default:
                    await s.Send("ERROR|Unknown command");
                    break;
            }
        }

        // ============================================================
        //                         AUTH
        // ============================================================

        private static async Task HandleAuth(ClientSession s, string[] parts)
        {
            if (parts.Length < 4)
                return;

            string mode = parts[1];
            string login = parts[2];
            string pass = parts[3];

            if (mode == "REGISTER")
            {
                bool registered = Database.TryRegisterUser(login, pass);

                if (!registered)
                {
                    await s.Send("AUTH|FAIL|User exists");
                    return;
                }

                s.Authenticate(login);
                await s.Send("AUTH|OK");
                ServerState.BroadcastUsers();
                return;
            }

            if (mode == "LOGIN")
            {
                bool ok = Database.CheckUserCredentials(login, pass);

                if (!ok)
                {
                    await s.Send("AUTH|FAIL|Invalid credentials");
                    return;
                }

                s.Authenticate(login);
                await s.Send("AUTH|OK");
                ServerState.BroadcastUsers();
                return;
            }

            // На всякий случай, если придёт что-то странное
            await s.Send("AUTH|FAIL|Unknown mode");
        }


        // ============================================================
        //                         MESSAGE
        // ============================================================

        private static async Task HandleMessage(ClientSession s, string[] parts)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            string receiver = parts[1];
            string msgText = string.Join("|", parts, 2, parts.Length - 2);

            long id = Database.SaveMessage(s.Username!, receiver, msgText);
            string time = DateTime.Now.ToString("HH:mm:ss");

            string formatted =
                $"MSG|{id}|{time}|{s.Username}|{receiver}|{msgText}";

            if (receiver == "all" || receiver == "ALL")
            {
                ServerState.Broadcast(formatted);
            }
            else
            {
                ClientSession? target;

                lock (ServerState.clients)
                {
                    target = ServerState.clients.FirstOrDefault(
                        c => c.IsAuthenticated && c.Username == receiver);
                }

                if (target != null)
                {
                    await target.Send(formatted);
                    await s.Send(formatted);
                }
                else
                {
                    await s.Send("ERROR|User not online");
                }
            }
        }


        // ============================================================
        //                        HISTORY
        // ============================================================

        private static async Task SendHistory(ClientSession s, string[] parts)
        {
            // HISTORY
            if (parts.Length == 1)
            {
                var list = Database.GetPersonalHistory(s.Username!);

                foreach (var m in list)
                    await s.Send($"MSG|{m.Id}|{m.Time}|{m.Sender}|{m.Receiver}|{m.Text}");

                await s.Send("--END--");
                return;
            }

            // HISTORY|PUBLIC
            if (parts.Length == 2 && parts[1] == "PUBLIC")
            {
                var list = Database.GetPublicHistory();

                foreach (var m in list)
                    await s.Send($"MSG|{m.Id}|{m.Time}|{m.Sender}|{m.Receiver}|{m.Text}");

                await s.Send("--END--");
                return;
            }

            // HISTORY|PM|username
            if (parts.Length == 3 && parts[1] == "PM")
            {
                string user2 = parts[2];
                var list = Database.GetPrivateHistory(s.Username!, user2);

                foreach (var m in list)
                    await s.Send($"MSG|{m.Id}|{m.Time}|{m.Sender}|{m.Receiver}|{m.Text}");

                await s.Send("--END--");
                return;
            }

            // HISTORY|ROOM|roomName — добавим позже
            if (parts.Length == 3 && parts[1] == "ROOM")
            {
                string room = parts[2];

                var list = Database.GetRoomHistory(room);

                foreach (var m in list)
                    await s.Send($"MSG|{m.Id}|{m.Time}|{m.Sender}|{m.Receiver}|{m.Text}");

                await s.Send("--END--");
                return;
            }

        }


        // ============================================================
        //                        FILE TRANSFER
        // ============================================================

        private static async Task HandleFile(ClientSession s, string[] parts)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            string receiver = parts[1];
            string filename = parts[2];
            long size = long.Parse(parts[3]);

            const long MAX_FILE_SIZE = 200L * 1024L * 1024L;

            if (size > MAX_FILE_SIZE)
            {
                await s.Send("ERROR|File too large (limit 200 MB). Connection will be closed.");
                s.Close();
                return;
            }

            ClientSession? target;

            lock (ServerState.clients)
            {
                target = ServerState.clients.FirstOrDefault(
                    c => c.IsAuthenticated && c.Username == receiver);
            }

            if (target == null)
            {
                await s.Send("ERROR|User not online");
                return;
            }

            await target.Send($"FILE|{s.Username}|{filename}|{size}");

            byte[] buffer = new byte[8192];
            long remaining = size;

            while (remaining > 0)
            {
                int read = await s.RawStream.ReadAsync(
                    buffer, 0,
                    (int)Math.Min(buffer.Length, remaining));

                if (read <= 0)
                    break;

                await target.RawStream.WriteAsync(buffer, 0, read);
                remaining -= read;
            }
        }


        // ============================================================
        //                        EDIT MESSAGE
        // ============================================================

        private static async Task HandleEdit(ClientSession s, string[] parts)
        {
            if (parts.Length < 3)
                return;

            if (!s.IsAuthenticated)
                return;

            long id = long.Parse(parts[1]);
            string newText = string.Join("|", parts.Skip(2)) + " (edited)";

            bool ok = Database.EditMessage(id, s.Username!, newText);

            if (!ok)
            {
                await s.Send("ERROR|Cannot edit");
                return;
            }

            // уведомляем всех о том, что история изменилась
            ServerState.Broadcast("HISTORY_UPDATED");
        }


        // ============================================================
        //                        DELETE MESSAGE
        // ============================================================

        private static async Task HandleDelete(ClientSession s, string[] parts)
        {
            if (parts.Length < 2)
                return;

            if (!s.IsAuthenticated)
                return;

            long id = long.Parse(parts[1]);

            bool ok = Database.DeleteMessage(id, s.Username!);

            if (!ok)
            {
                await s.Send("ERROR|Cannot delete");
                return;
            }

            ServerState.Broadcast("HISTORY_UPDATED");
        }

        private static async Task HandleRoomCreate(ClientSession s, string[] parts)
        {
            // Должен быть авторизован
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            // Формат команды: ROOM_CREATE|name
            if (parts.Length < 2)
            {
                await s.Send("ERROR|Room name required");
                return;
            }

            string roomName = parts[1];

            // Проверяем, существует ли комната
            if (Database.RoomExists(roomName))
            {
                await s.Send($"ROOM|EXISTS|{roomName}");
                return;
            }

            // Создаём комнату
            long roomId = Database.CreateRoom(roomName, s.Username!);

            // Добавляем создателя в участники
            Database.AddUserToRoom(roomId, s.Username!);

            // Отвечаем клиенту
            await s.Send($"ROOM|CREATED|{roomName}");
        }
        // ============================================================
        //                        ROOM_DELETE
        // ============================================================
        private static async Task HandleRoomDelete(ClientSession s, string[] parts)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            if (parts.Length < 2)
            {
                await s.Send("ERROR|Usage: ROOM_DELETE|roomName");
                return;
            }

            string roomName = parts[1];

            // 1. Проверяем, есть ли такая комната
            RoomRecord? room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|Room not found");
                return;
            }

            // 2. Проверяем, что текущий пользователь — владелец
            if (!string.Equals(room.Owner, s.Username, StringComparison.OrdinalIgnoreCase))
            {
                await s.Send("ERROR|Only owner can delete room");
                return;
            }

            // 3. Удаляем комнату и всех участников из RoomMembers
            Database.DeleteRoomById(room.Id);

            // 4. Сообщаем клиенту (и при желании можно добавить широковещалку)
            await s.Send($"ROOM|DELETED|{roomName}");
        }
        // ============================================================
        //                        ROOM_JOIN
        // ============================================================

        private static async Task HandleRoomJoin(ClientSession s, string[] parts)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            if (parts.Length < 2)
            {
                await s.Send("ERROR|Room name required");
                return;
            }

            string roomName = parts[1];

            // 1. Проверяем, что комната существует
            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            // 2. Проверяем, что пользователь ещё не в комнате
            bool already = Database.IsUserInRoom(room.Id, s.Username!);
            if (already)
            {
                await s.Send($"ROOM|ALREADY|{roomName}");
                return;
            }

            // 3. Добавляем в RoomMembers (INSERT OR IGNORE стоит в Database)
            Database.AddUserToRoom(room.Id, s.Username!);

            // 4. Сообщаем клиенту
            await s.Send($"ROOM|JOINED|{roomName}");
        }
        // ============================================================
        //                        ROOM_LEAVE
        // ============================================================

        private static async Task HandleRoomLeave(ClientSession s, string[] parts)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            if (parts.Length < 2)
            {
                await s.Send("ERROR|Room name required");
                return;
            }

            string roomName = parts[1];

            // 1. Проверяем, существует ли комната
            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            // 2. Проверяем членство
            bool isMember = Database.IsUserInRoom(room.Id, s.Username!);
            if (!isMember)
            {
                await s.Send($"ROOM|NOT_MEMBER|{roomName}");
                return;
            }

            // 3. Удаляем участника из RoomMembers
            Database.RemoveUserFromRoom(room.Id, s.Username!);

            // 4. Сообщаем клиенту
            await s.Send($"ROOM|LEFT|{roomName}");
        }
        // ============================================================
        //                      ROOM MESSAGE
        // ============================================================

        private static async Task HandleRoomMessage(ClientSession s, string[] parts)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            // ROOM_MSG|roomName|text...
            if (parts.Length < 3)
                return;

            string roomName = parts[1];
            string msgText = string.Join("|", parts, 2, parts.Length - 2);

            // 1. Проверяем, что комната существует
            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|Room not found");
                return;
            }

            // 2. Проверяем, что отправитель состоит в комнате
            if (s.Username == null || !Database.IsUserInRoom(room.Id, s.Username))
            {
                await s.Send("ERROR|You are not in this room");
                return;
            }

            // 3. Сохраняем сообщение в БД:
            //    Sender = текущий пользователь
            //    Receiver = имя комнаты
            long id = Database.SaveMessage(s.Username, roomName, msgText);
            string time = DateTime.Now.ToString("HH:mm:ss");

            string formatted = $"MSG|{id}|{time}|{s.Username}|{roomName}|{msgText}";

            // 4. Выбираем всех участников комнаты из БД
            var members = Database.GetRoomMembers(room.Id); // List<string> с логинами

            // 5. Отправляем сообщение всем онлайн-участникам комнаты
            lock (ServerState.clients)
            {
                foreach (var client in ServerState.clients
                             .Where(c => c.IsAuthenticated
                                         && !string.IsNullOrEmpty(c.Username)
                                         && members.Contains(c.Username!)))
                {
                    try
                    {
                        _ = client.Send(formatted);
                    }
                    catch
                    {
                        // проглатываем ошибки отправки, чтобы не уронить сервер
                    }
                }
            }
        }
    }
}
