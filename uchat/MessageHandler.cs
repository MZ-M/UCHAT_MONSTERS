using System;
using System.IO;

namespace uchat_client
{
    class MessageHandler
    {
        public static void ProcessMessage(string msg)
        {
            try
            {
                // --------------------------------------------------
                // USERS LIST (ONLINE)
                // --------------------------------------------------
                if (msg.StartsWith("USERS|"))
                {
                    Console.WriteLine("[ONLINE] " + msg.Substring(6));
                    return;
                }

                // --------------------------------------------------
                // ROOM LIST (ROOM_LIST|...)
                // --------------------------------------------------
                if (msg.StartsWith("ROOM_LIST|"))
                {
                    string list = msg.Substring("ROOM_LIST|".Length);

                    if (list == "NONE")
                        Console.WriteLine("[ROOM LIST] No rooms exist.");
                    else
                        Console.WriteLine("[ROOM LIST] " + list);

                    return;
                }

                // --------------------------------------------------
                // FILE TRANSFER
                // --------------------------------------------------
                if (msg.StartsWith("FILE|"))
                {
                    HandleIncomingFile(msg);
                    return;
                }

                // --------------------------------------------------
                // ROOM INFO
                // ROOM_INFO|name|owner|createdAt|count|member1,member2,...
                // --------------------------------------------------
                if (msg.StartsWith("ROOM_INFO|"))
                {
                    var p = msg.Split('|');

                    if (p.Length < 6)
                    {
                        Console.WriteLine("[ROOM INFO] Invalid data");
                        return;
                    }

                    string name = p[1];
                    string owner = p[2];
                    string createdAt = p[3];
                    string count = p[4];
                    string members = p[5];

                    Console.WriteLine($"\n=== ROOM INFO: {name} ===");
                    Console.WriteLine($"Owner:      {owner}");
                    Console.WriteLine($"Created:    {createdAt}");
                    Console.WriteLine($"Members:    {count}");

                    if (members == "NONE")
                    {
                        Console.WriteLine("Member list: (empty)");
                    }
                    else
                    {
                        Console.WriteLine("Member list:");
                        foreach (var m in members.Split(','))
                            Console.WriteLine("  • " + m);
                    }

                    Console.WriteLine("==========================\n");

                    return;
                }

                // --------------------------------------------------
                // ROOM KICK
                // --------------------------------------------------
                if (msg.StartsWith("ROOM_KICK|"))
                {
                    var p = msg.Split('|');

                    if (p.Length < 3)
                    {
                        Console.WriteLine("[ROOM KICK] Invalid format.");
                        return;
                    }

                    string status = p[1];

                    // owner got confirmation
                    if (status == "OK")
                    {
                        string room = p[2];
                        string target = p[3];

                        Console.WriteLine($"[ROOM KICK] User '{target}' was kicked from {room}");
                        return;
                    }

                    // kicked user receives KICKED
                    if (status == "KICKED")
                    {
                        string room = p[2];
                        Console.WriteLine($"[ROOM KICK] You were kicked from room '{room}'");

                        // автоматически переводим kicked-пользователя в public chat
                        if (Program.chatMode == $"room:{room}")
                        {
                            Program.chatMode = "all";
                            Console.WriteLine("[MODE] Changed to public chat");
                        }

                        return;
                    }

                    Console.WriteLine("[ROOM KICK] Unknown format: " + msg);
                    return;
                }

                // --------------------------------------------------
                // ROOM_RENAME (OK for owner, RENAMED for members)
                // --------------------------------------------------
                if (msg.StartsWith("ROOM_RENAME|"))
                {
                    var p = msg.Split('|');

                    if (p.Length < 4)
                    {
                        Console.WriteLine("[ROOM RENAME] Invalid format.");
                        return;
                    }

                    string status = p[1];
                    string oldName = p[2];
                    string newName = p[3];

                    // инициатор успешного переименования
                    if (status == "OK")
                    {
                        Console.WriteLine($"[ROOM RENAME] '{oldName}' renamed to '{newName}'");

                        // если клиент находился в комнате oldName — переключаем режим
                        if (Program.chatMode == $"room:{oldName}")
                        {
                            Program.chatMode = $"room:{newName}";
                            Console.WriteLine($"[MODE] Switched to room '{newName}'");
                        }

                        return;
                    }

                    // уведомление участникам комнаты
                    if (status == "RENAMED")
                    {
                        Console.WriteLine($"[ROOM] Room '{oldName}' renamed to '{newName}'");

                        // если клиент находился в oldName — переключить
                        if (Program.chatMode == $"room:{oldName}")
                        {
                            Program.chatMode = $"room:{newName}";
                            Console.WriteLine($"[MODE] Switched to room '{newName}'");
                        }

                        return;
                    }

                    Console.WriteLine("[ROOM RENAME] Unknown response: " + msg);
                    return;
                }


                // --------------------------------------------------
                // PUBLIC / PRIVATE / ROOM MESSAGE
                // Format: MSG|id|time|sender|receiver|text
                // --------------------------------------------------
                if (msg.StartsWith("MSG|"))
                {
                    HandleChatMessage(msg);
                    return;
                }
                // --------------------------------------------------
                // ROOM ACTIONS (CREATED / JOINED / DELETED / ETC)
                // --------------------------------------------------
                if (msg.StartsWith("ROOM|"))
                {
                    HandleRoomEvent(msg);
                    return;
                }

                // --------------------------------------------------
                // SERVER ERROR
                // --------------------------------------------------
                if (msg.StartsWith("ERROR|"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] " + msg.Substring(6));
                    Console.ResetColor();
                    return;
                }

                // --------------------------------------------------
                // DEFAULT OUTPUT
                // --------------------------------------------------
                Console.WriteLine(msg);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CLIENT ERROR] " + ex.Message);
            }
        }

        // ================================================================
        // CHAT MESSAGE HANDLING
        // ================================================================
        private static void HandleChatMessage(string msg)
        {
            var p = msg.Split('|', 6);

            long id = long.Parse(p[1]);
            string time = p[2];
            string sender = p[3];
            string receiver = p[4];
            string text = p[5];

            Console.WriteLine($"[{id}] {time} | {sender} -> {receiver}: {text}");
        }

        // ================================================================
        // ROOM NOTIFICATIONS
        // ================================================================
        private static void HandleRoomEvent(string msg)
        {
            var args = msg.Split('|');

            if (args.Length < 3)
            {
                Console.WriteLine("[ROOM] Invalid message: " + msg);
                return;
            }

            string action = args[1];
            string room = args[2];

            switch (action)
            {
                case "CREATED":
                    Console.WriteLine($"[ROOM] Created: {room}");
                    break;

                case "DELETED":
                    Console.WriteLine($"[ROOM] Deleted: {room}");
                    break;

                case "JOINED":
                    Console.WriteLine($"[ROOM] Joined: {room}");
                    break;

                case "LEFT":
                    Console.WriteLine($"[ROOM] Left: {room}");
                    break;

                case "EXISTS":
                    Console.WriteLine($"[ROOM] Already exists: {room}");
                    break;

                case "ALREADY":
                    Console.WriteLine($"[ROOM] Already a member of: {room}");
                    break;

                case "NOT_MEMBER":
                    Console.WriteLine($"[ROOM] Not a member: {room}");
                    break;

                case "USERS":
                    {
                        if (args.Length < 4)
                        {
                            Console.WriteLine("[ROOM USERS] Invalid format.");
                            break;
                        }

                        string users = args[3];
                        if (users == "NONE")
                            Console.WriteLine($"[ROOM USERS] {room}: no members.");
                        else
                            Console.WriteLine($"[ROOM USERS] {room}: {users}");

                        break;
                    }



                default:
                    Console.WriteLine("[ROOM] " + msg);
                    break;
            }
        }

        // ================================================================
        // FILE DOWNLOAD HANDLING
        // ================================================================
        private static void HandleIncomingFile(string msg)
        {
            var p = msg.Split('|');

            string sender = p[1];
            string file = p[2];
            long size = long.Parse(p[3]);

            Console.WriteLine($"[FILE] {sender} → {file} ({size} bytes)");

            string dir = Path.Combine(Environment.CurrentDirectory, "downloads");
            Directory.CreateDirectory(dir);

            string save = Path.Combine(dir, file);

            using var fs = new FileStream(save, FileMode.Create);

            byte[] buf = new byte[8192];
            long remain = size;

            while (remain > 0)
            {
                int read = Program.reader.BaseStream.Read(
                    buf, 0, (int)Math.Min(buf.Length, remain));

                if (read <= 0) break;

                fs.Write(buf, 0, read);
                remain -= read;
            }

            Console.WriteLine($"[SAVED] {save}");
        }
    }
}