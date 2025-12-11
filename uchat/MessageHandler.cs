using System;
using System.IO;
using System.Threading.Tasks;

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
                // FILE PROTOCOL (НОВЫЙ)
                // --------------------------------------------------
                if (msg.StartsWith("FILE_DENIED|"))
                {
                    string who = msg.Split('|')[1];

                    Console.WriteLine($"[FILE] {who} denied your file.");

                    PendingFileSend.Reset(); 
                    return;
                }

                if (msg.StartsWith("FILE_UPLOAD_READY|"))
                {
                    var p = msg.Split('|');
                    string fileId = p[1];
                    string filename = p[2];
                    long size = long.Parse(p[3]);

                    Console.WriteLine($"[FILE] Server is ready. Uploading file: {filename} ({size} bytes)");

                    PendingFileSend.FileId = fileId;
                    PendingFileSend.StartSending();

                    return;
                }

                if (msg.StartsWith("FILE_STORED|"))
                {
                    var p = msg.Split('|');
                    string fileId = p.Length > 1 ? p[1] : "?";

                    Console.WriteLine($"[FILE] File stored on server (id={fileId}).");
                    return;
                }

                // ================================
                // NEW: FILE_OFFER
                // ================================
                if (msg.StartsWith("FILE_OFFER|"))
                {
                    var p = msg.Split('|');

                    string fileId = p[1];
                    string sender = p[2];
                    string fileName = p[3];
                    long size = long.Parse(p[4]);

                    Console.WriteLine("\n[FILE OFFER]");
                    Console.WriteLine($"Sender: {sender}");
                    Console.WriteLine($"File:   {fileName}");
                    Console.WriteLine($"Size:   {size} bytes");
                    Console.WriteLine($"Use: /y   OR   /n");

                    PendingReceive.Set(fileId, sender, fileName, size);
                    return;
                }

                // ================================
                // NEW: FILE_BEGIN
                // ================================
                if (msg.StartsWith("FILE_BEGIN|"))
                {
                    var p = msg.Split('|');
                    string fileName = p[1];
                    long size = long.Parse(p[2]);

                    Console.WriteLine($"[FILE] Server begins sending: {fileName} ({size} bytes)");

                    FileReceiver.Start(fileName, size);
                    return;
                }

                if (msg.StartsWith("FILE_DONE|"))
                {
                    var p = msg.Split('|');
                    string fileId = p[1];

                    Console.WriteLine($"[FILE] Transfer completed (id={fileId})");

                    FileReceiver.Reset();
                    PendingReceive.Reset();
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

                    if (status == "OK")
                    {
                        string room = p[2];
                        string target = p[3];

                        Console.WriteLine($"[ROOM KICK] User '{target}' was kicked from {room}");
                        return;
                    }

                    if (status == "KICKED")
                    {
                        string room = p[2];
                        Console.WriteLine($"[ROOM KICK] You were kicked from room '{room}'");

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

                    if (status == "OK")
                    {
                        Console.WriteLine($"[ROOM RENAME] '{oldName}' renamed to '{newName}'");

                        if (Program.chatMode == $"room:{oldName}")
                        {
                            Program.chatMode = $"room:{newName}";
                            Console.WriteLine($"[MODE] Switched to room '{newName}'");
                        }

                        return;
                    }

                    if (status == "RENAMED")
                    {
                        Console.WriteLine($"[ROOM] Room '{oldName}' renamed to '{newName}'");

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

    }
}
