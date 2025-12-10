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

                case "ROOM_LIST":
                    await HandleRoomList(s);
                    break;

                case "ROOM_USERS":
                    await HandleRoomUsers(s, parts);
                    break;

                case "ROOM_INFO":
                    await HandleRoomInfo(s, parts);
                    break;

                case "ROOM_KICK":
                    await HandleRoomKick(s, parts);
                    break;

                case "ROOM_RENAME":
                    await HandleRoomRename(s, parts);
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
            try
            {
                if (parts.Length < 4)
                {
                    await s.Send("AUTH|FAIL|Invalid command format");
                    return;
                }

                string mode = parts[1];
                string login = parts[2];
                string pass = parts[3];

                if (mode == "REGISTER")
                {
                    if (!PasswordPolicy.IsStrong(pass, out var passwordError))
                    {
                        await s.Send($"AUTH|FAIL|{passwordError}");
                        return;
                    }

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
            catch (Exception ex)
            {
                await s.Send($"AUTH|FAIL|Database error: {ex.Message}");
            }
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
            if (id < 0)
            {
                await s.Send("ERROR|Invalid receiver");
                return;
            }
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
            if (!string.Equals(room.OwnerUsername, s.Username, StringComparison.OrdinalIgnoreCase))
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
            if (id < 0)
            {
                await s.Send("ERROR|Room not found");
                return;
            }
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
        // ============================================================
        //                        ROOM_LIST
        // ============================================================
        private static async Task HandleRoomList(ClientSession s)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            var rooms = Database.GetAllRooms();

            if (rooms.Count == 0)
            {
                await s.Send("ROOM_LIST|NONE");
                return;
            }

            string list = string.Join(",", rooms.Select(r => $"{r.Name}({r.OwnerUsername})"));
            await s.Send($"ROOM_LIST|{list}");
        }
        // ============================================================
        //                      ROOM_USERS
        // ============================================================
        private static async Task HandleRoomUsers(ClientSession s, string[] parts)
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

            // 1. Проверяем существование комнаты
            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            // 2. Получаем участников комнаты
            var members = Database.GetRoomMembers(room.Id);

            if (members.Count == 0)
            {
                await s.Send($"ROOM_USERS|{roomName}|NONE");
                return;
            }

            string list = string.Join(",", members);

            await s.Send($"ROOM_USERS|{roomName}|{list}");
        }
        // ============================================================
        //                        ROOM_INFO
        // ============================================================
        private static async Task HandleRoomInfo(ClientSession s, string[] parts)
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

            // 1. Получаем комнату
            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            // 2. Получаем участников комнаты
            var members = Database.GetRoomMembers(room.Id);
            int count = members.Count;

            // 3. Формируем ответ
            // Формат: ROOM_INFO|name|owner|date|count|member1,member2,...
            string list = count > 0 ? string.Join(",", members) : "NONE";

            await s.Send($"ROOM_INFO|{room.Name}|{room.OwnerUsername}|{room.CreatedAt}|{count}|{list}");
        }
        // ============================================================
        //                        ROOM_KICK
        // ============================================================

        private static async Task HandleRoomKick(ClientSession s, string[] parts)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            // Формат: ROOM_KICK|roomName|username
            if (parts.Length < 3)
            {
                await s.Send("ERROR|Usage: ROOM_KICK|roomName|username");
                return;
            }

            string roomName = parts[1];
            string targetUser = parts[2];

            // 1. Проверяем, существует ли комната
            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            // 2. Проверяем, что текущий юзер — владелец комнаты
            if (!string.Equals(room.OwnerUsername, s.Username, StringComparison.OrdinalIgnoreCase))
            {
                await s.Send("ERROR|NotOwner");
                return;
            }

            // 3. Владелец не может кикнуть самого себя
            if (string.Equals(room.OwnerUsername, targetUser, StringComparison.OrdinalIgnoreCase))
            {
                await s.Send("ERROR|OwnerCannotBeKicked");
                return;
            }

            // 4. Проверяем, что юзер является участником комнаты
            bool isMember = Database.IsUserInRoom(room.Id, targetUser);
            if (!isMember)
            {
                await s.Send("ERROR|UserNotInRoom");
                return;
            }

            // 5. Удаляем пользователя из комнаты
            Database.RemoveUserFromRoom(room.Id, targetUser);

            // 6. Отправляем ответ инициатору
            await s.Send($"ROOM_KICK|OK|{roomName}|{targetUser}");

            // 7. Если кикнутый юзер онлайн — сообщаем ему
            lock (ServerState.clients)
            {
                var kickedClient = ServerState.clients
                    .FirstOrDefault(c =>
                        c.IsAuthenticated &&
                        c.Username != null &&
                        c.Username.Equals(targetUser, StringComparison.OrdinalIgnoreCase));

                if (kickedClient != null)
                    _ = kickedClient.Send($"ROOM_KICK|KICKED|{roomName}");
            }
        }
        // ============================================================
        //                      ROOM_RENAME
        // ============================================================

        private static async Task HandleRoomRename(ClientSession s, string[] parts)
        {
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            // Формат: ROOM_RENAME|oldName|newName
            if (parts.Length < 3)
            {
                await s.Send("ERROR|Usage: ROOM_RENAME|oldName|newName");
                return;
            }

            string oldName = parts[1];
            string newName = parts[2];

            // 1. Получить старую комнату
            var room = Database.GetRoomByName(oldName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            // 2. Проверить, что пользователь — владелец комнаты
            if (!string.Equals(room.OwnerUsername, s.Username, StringComparison.OrdinalIgnoreCase))
            {
                await s.Send("ERROR|NotOwner");
                return;
            }

            // 3. Проверить, что новое имя не занято
            if (Database.RoomExists(newName))
            {
                await s.Send("ERROR|NameExists");
                return;
            }

            // 4. Переименовать комнату
            bool ok = Database.RenameRoom(oldName, newName);
            if (!ok)
            {
                await s.Send("ERROR|RenameFailed");
                return;
            }

            // 5. Обновить имя в истории сообщений
            Database.UpdateRoomMessagesName(oldName, newName);

            // 6. Ответ пользователю
            await s.Send($"ROOM_RENAME|OK|{oldName}|{newName}");

            // 7. Получаем всех участников комнаты
            var members = Database.GetRoomMembers(room.Id);

            // 8. Уведомляем всех участников, кроме инициатора
            foreach (var username in members)
            {
                if (username.Equals(s.Username, StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = ServerState.clients
                    .FirstOrDefault(c => c.IsAuthenticated &&
                                         c.Username != null &&
                                         c.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (target != null)
                    _ = target.Send($"ROOM_RENAME|RENAMED|{oldName}|{newName}");
            }
        }








    }
}
