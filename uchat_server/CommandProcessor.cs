using System.IO;
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
        private const long MAX_FILE_SIZE = 200L * 1024L * 1024L; // 200 MB
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

                case "FILE_ACCEPT":
                    await HandleFileAccept(s, parts);
                    break;

                case "FILE_DENY":
                    await HandleFileDeny(s, parts);
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

                    // ------- SEND PENDING FILE OFFERS -------
                    string username = s.Username!;

                    var pending = ServerState.PendingFiles
                        .Where(f => f.Receivers.Contains(username,
                                        StringComparer.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var f in pending)
                    {
                        await s.Send(
                            $"FILE_OFFER|{f.FileId}|{f.Sender}|{f.FileName}|{f.FileSize}");
                    }

                    return;
                }

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

            // HISTORY|ROOM|roomName
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

            if (parts.Length < 4)
            {
                await s.Send("ERROR|Bad FILE format");
                return;
            }

            string receiverRaw = parts[1]; 
            string filename = parts[2];
            string sizeStr = parts[3];

            if (!long.TryParse(sizeStr, out long size) || size <= 0)
            {
                await s.Send("ERROR|Bad file size");
                return;
            }

            if (size > MAX_FILE_SIZE)
            {
                await s.Send("ERROR|File too large (limit 200 MB)");
                return;
            }

            if (s.Username == null)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            bool isRoom = receiverRaw.StartsWith("#");
            string? roomName = null;
            List<string> receivers = new();

            if (isRoom)
            {
                roomName = receiverRaw.Substring(1);

                if (!ServerState.Rooms.ContainsKey(roomName))
                {
                    await s.Send($"ERROR|Room '{roomName}' does not exist");
                    return;
                }

                if (!ServerState.Rooms[roomName].Contains(s.Username))
                {
                    await s.Send($"ERROR|You are not a member of room '{roomName}'");
                    return;
                }

                receivers = ServerState.Rooms[roomName]
                    .Where(u => !u.Equals(s.Username, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (receivers.Count == 0)
                {
                    await s.Send("ERROR|Room has no other participants");
                    return;
                }
            }
            else
            {

                string targetUser = receiverRaw;

                if (!Database.UserExists(targetUser))
                {
                    await s.Send($"ERROR|User '{targetUser}' does not exist");
                    return;
                }

                if (targetUser.Equals(s.Username, StringComparison.OrdinalIgnoreCase))
                {
                    await s.Send("ERROR|Cannot send file to yourself");
                    return;
                }

                receivers.Add(targetUser);
            }

            Directory.CreateDirectory(Program.TempDir);

            var sf = new StoredFile
            {
                Sender     = s.Username!,
                FileName   = filename,
                FileSize   = size,
                FilePath   = Path.Combine(
                                Program.TempDir,
                                Guid.NewGuid().ToString("N") + "_" + filename
                             ),

                IsRoomFile = isRoom,
                RoomName   = roomName,

                Receivers = receivers
            };

            File.Create(sf.FilePath).Close();

            s.ActiveUpload = sf;

            await s.Send($"FILE_UPLOAD_READY|{sf.FileId}|{filename}|{size}");
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
            if (!s.IsAuthenticated)
            {
                await s.Send("ERROR|Not authorized");
                return;
            }

            // ROOM_CREATE|name
            if (parts.Length < 2)
            {
                await s.Send("ERROR|Room name required");
                return;
            }

            string roomName = parts[1];

            if (Database.RoomExists(roomName))
            {
                await s.Send($"ROOM|EXISTS|{roomName}");
                return;
            }

            long roomId = Database.CreateRoom(roomName, s.Username!);

            Database.AddUserToRoom(roomId, s.Username!);

            ServerState.Rooms[roomName] =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
            s.Username!
                };

            await s.Send($"ROOM|CREATED|{roomName}");

            foreach (var c in ServerState.clients.Where(c => c.IsAuthenticated))
            {
                if (c != s)
                    _ = c.Send($"ROOM_UPDATE|CREATED|{roomName}|{s.Username}");
            }

            Console.WriteLine($"[ROOM] Created '{roomName}' by {s.Username}");
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

            RoomRecord? room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|Room not found");
                return;
            }

            if (!string.Equals(room.OwnerUsername, s.Username, StringComparison.OrdinalIgnoreCase))
            {
                await s.Send("ERROR|Only owner can delete room");
                return;
            }

            Database.DeleteRoomById(room.Id);

            if (ServerState.Rooms.ContainsKey(roomName))
                ServerState.Rooms.Remove(roomName);

            await s.Send($"ROOM|DELETED|{roomName}");

            Console.WriteLine($"[ROOM] Deleted '{roomName}' by {s.Username}");

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

            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            bool already = Database.IsUserInRoom(room.Id, s.Username!);
            if (already)
            {
                await s.Send($"ROOM|ALREADY|{roomName}");
                return;
            }

            Database.AddUserToRoom(room.Id, s.Username!);

            if (!ServerState.Rooms.ContainsKey(roomName))
            {
                ServerState.Rooms[roomName] =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            ServerState.Rooms[roomName].Add(s.Username!);

            await s.Send($"ROOM|JOINED|{roomName}");

            foreach (var username in ServerState.Rooms[roomName])
            {
                if (username.Equals(s.Username, StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = ServerState.clients
                    .FirstOrDefault(c => c.IsAuthenticated &&
                                         c.Username != null &&
                                         c.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (target != null)
                    _ = target.Send($"ROOM|USER_JOINED|{roomName}|{s.Username}");
            }

            Console.WriteLine($"[ROOM] {s.Username} joined '{roomName}'");
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

            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            bool isMember = Database.IsUserInRoom(room.Id, s.Username!);
            if (!isMember)
            {
                await s.Send($"ROOM|NOT_MEMBER|{roomName}");
                return;
            }

            Database.RemoveUserFromRoom(room.Id, s.Username!);

            if (ServerState.Rooms.ContainsKey(roomName))
                ServerState.Rooms[roomName].Remove(s.Username!);

            await s.Send($"ROOM|LEFT|{roomName}");

            if (ServerState.Rooms.ContainsKey(roomName))
            {
                foreach (var username in ServerState.Rooms[roomName])
                {
                    var target = ServerState.clients
                        .FirstOrDefault(c => c.IsAuthenticated &&
                                             c.Username != null &&
                                             c.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                    if (target != null)
                        _ = target.Send($"ROOM|USER_LEFT|{roomName}|{s.Username}");
                }
            }

            Console.WriteLine($"[ROOM] {s.Username} left '{roomName}'");
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

            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|Room not found");
                return;
            }

            if (s.Username == null || !Database.IsUserInRoom(room.Id, s.Username))
            {
                await s.Send("ERROR|You are not in this room");
                return;
            }

            long id = Database.SaveMessage(s.Username, roomName, msgText);
            if (id < 0)
            {
                await s.Send("ERROR|Room not found");
                return;
            }
            string time = DateTime.Now.ToString("HH:mm:ss");

            string formatted = $"MSG|{id}|{time}|{s.Username}|{roomName}|{msgText}";

            var members = Database.GetRoomMembers(room.Id);

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

            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

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

            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            var members = Database.GetRoomMembers(room.Id);
            int count = members.Count;

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

            // ROOM_KICK|roomName|username
            if (parts.Length < 3)
            {
                await s.Send("ERROR|Usage: ROOM_KICK|roomName|username");
                return;
            }

            string roomName = parts[1];
            string targetUser = parts[2];

            var room = Database.GetRoomByName(roomName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            if (!string.Equals(room.OwnerUsername, s.Username, StringComparison.OrdinalIgnoreCase))
            {
                await s.Send("ERROR|NotOwner");
                return;
            }

            if (string.Equals(room.OwnerUsername, targetUser, StringComparison.OrdinalIgnoreCase))
            {
                await s.Send("ERROR|OwnerCannotBeKicked");
                return;
            }

            bool isMember = Database.IsUserInRoom(room.Id, targetUser);
            if (!isMember)
            {
                await s.Send("ERROR|UserNotInRoom");
                return;
            }

            Database.RemoveUserFromRoom(room.Id, targetUser);

            if (ServerState.Rooms.ContainsKey(roomName))
                ServerState.Rooms[roomName].Remove(targetUser);

            await s.Send($"ROOM_KICK|OK|{roomName}|{targetUser}");

            ClientSession? kickedClient = null;

            lock (ServerState.clients)
            {
                kickedClient = ServerState.clients
                    .FirstOrDefault(c =>
                        c.IsAuthenticated &&
                        c.Username != null &&
                        c.Username.Equals(targetUser, StringComparison.OrdinalIgnoreCase));
            }

            if (kickedClient != null)
                _ = kickedClient.Send($"ROOM_KICK|KICKED|{roomName}");

            foreach (var username in ServerState.Rooms[roomName])
            {
                if (username.Equals(s.Username, StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = ServerState.clients
                    .FirstOrDefault(c =>
                        c.IsAuthenticated &&
                        c.Username != null &&
                        c.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (target != null)
                    _ = target.Send($"ROOM|USER_KICKED|{roomName}|{targetUser}");
            }

            Console.WriteLine($"[ROOM] {targetUser} was kicked from '{roomName}' by {s.Username}");
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

            if (parts.Length < 3)
            {
                await s.Send("ERROR|Usage: ROOM_RENAME|oldName|newName");
                return;
            }

            string oldName = parts[1];
            string newName = parts[2];

            var room = Database.GetRoomByName(oldName);
            if (room == null)
            {
                await s.Send("ERROR|RoomNotFound");
                return;
            }

            if (!string.Equals(room.OwnerUsername, s.Username, StringComparison.OrdinalIgnoreCase))
            {
                await s.Send("ERROR|NotOwner");
                return;
            }

            if (Database.RoomExists(newName))
            {
                await s.Send("ERROR|NameExists");
                return;
            }

            bool ok = Database.RenameRoom(oldName, newName);
            if (!ok)
            {
                await s.Send("ERROR|RenameFailed");
                return;
            }

            Database.UpdateRoomMessagesName(oldName, newName);

            if (ServerState.Rooms.ContainsKey(oldName))
            {
                var users = ServerState.Rooms[oldName];
                ServerState.Rooms.Remove(oldName);
                ServerState.Rooms[newName] = users;
            }

            await s.Send($"ROOM_RENAME|OK|{oldName}|{newName}");

            var members = Database.GetRoomMembers(room.Id);

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
            Console.WriteLine($"[ROOM] Renamed '{oldName}' → '{newName}' by {s.Username}");
        }
        private static async Task HandleFileAccept(ClientSession receiver, string[] parts)
        {
            if (parts.Length < 2)
                return;

            string fileId = parts[1];

            var sf = ServerState.PendingFiles
                   .FirstOrDefault(f =>
                    f.FileId == fileId &&
                    f.Receivers.Any(r => r.Equals(receiver.Username, StringComparison.OrdinalIgnoreCase)));

            if (sf == null)
            {
                await receiver.Send("ERROR|FILE_NOT_FOUND");
                return;
            }

            await receiver.Send($"FILE_BEGIN|{sf.FileName}|{sf.FileSize}");

            try
            {
                byte[] buffer = new byte[8192];

                using (var fs = new FileStream(sf.FilePath, FileMode.Open, FileAccess.Read))
                {
                    int read;
                    while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        byte[] chunk = new byte[read];
                        Buffer.BlockCopy(buffer, 0, chunk, 0, read);

                        FrameIO.WriteFrame(receiver.Writer,
                            new Frame(FrameType.FileChunk, chunk));
                    }
                }

                await receiver.Send($"FILE_DONE|{fileId}");

                sf.Accepted.Add(receiver.Username!);

                if (sf.Accepted.Count + sf.Denied.Count == sf.Receivers.Count)
                {
                    try { File.Delete(sf.FilePath); } catch { }

                    ServerState.PendingFiles.Remove(sf);

                    Console.WriteLine($"[FILE] All receivers done. Removed: {sf.FileName}");
                }
                else
                {
                    Console.WriteLine($"[FILE] delivered to {receiver.Username}: {sf.FileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERR] delivery failed: " + ex.Message);
                await receiver.Send("ERROR|FILE_DELIVERY_FAILED");
            }
        }

        private static async Task HandleFileDeny(ClientSession receiver, string[] parts)
        {
            if (parts.Length < 2)
                return;

            string fileId = parts[1];

            var sf = ServerState.PendingFiles
                .FirstOrDefault(f =>
                    f.FileId == fileId &&
                    f.Receivers.Any(r =>
                        r.Equals(receiver.Username, StringComparison.OrdinalIgnoreCase)));

            if (sf == null)
            {
                await receiver.Send("ERROR|FILE_NOT_FOUND");
                return;
            }

            sf.Denied.Add(receiver.Username!);

            Console.WriteLine($"[FILE] {receiver.Username} denied file {sf.FileName}");

            var sender = ServerState.clients.FirstOrDefault(c => c.Username == sf.Sender);
            if (sender != null)
            {
                await sender.Send($"FILE_DENIED|{receiver.Username}|{sf.FileName}");
            }

            if (sf.Accepted.Count + sf.Denied.Count == sf.Receivers.Count)
            {
                try { File.Delete(sf.FilePath); } catch { }
                ServerState.PendingFiles.Remove(sf);

                Console.WriteLine($"[FILE] All receivers done. Removed: {sf.FileName}");
            }
        }
    }
}
