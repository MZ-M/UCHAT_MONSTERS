using System;
using System.IO;

namespace uchat_client
{
    static class Commands
    {
        // ---------- PUBLIC CHAT ----------
        public static void SendPublic(string text)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"MSG|all|{text}");
        }

        // ---------- PRIVATE ----------
        public static void SendPrivate(string user, string text)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"MSG|{user}|{text}");
        }

        // ---------- FILE ----------
        public static void SendFile(string target, string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("File not found.");
                return;
            }

            string fn = Path.GetFileName(path);
            long size = new FileInfo(path).Length;

            lock (Program.writerLock)
                Program.writer.WriteLine($"FILE|{target}|{fn}|{size}");

            using var fs = File.OpenRead(path);
            fs.CopyTo(Program.writer.BaseStream);

            Console.WriteLine($"[FILE SENT] {fn}");
        }

        // ---------- EDIT ----------
        public static void EditMessage(string id, string text)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"EDIT|{id}|{text}");
        }

        // ---------- DELETE ----------
        public static void DeleteMessage(string id)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"DEL|{id}");
        }

        // ---------- HISTORY ----------
        public static void RequestHistory()
        {
            lock (Program.writerLock)
            {
                if (Program.chatMode == "all")
                {
                    Program.writer.WriteLine("HISTORY|PUBLIC");
                }
                else if (Program.chatMode.StartsWith("pm:"))
                {
                    string user = Program.chatMode.Substring(3);
                    Program.writer.WriteLine($"HISTORY|PM|{user}");
                }
                else if (Program.chatMode.StartsWith("room:"))
                {
                    string room = Program.chatMode.Substring(5);
                    Program.writer.WriteLine($"HISTORY|ROOM|{room}");
                }
            }
        }

        // ---------- ROOMS ----------
        public static void RequestRoomList()
        {
            lock (Program.writerLock)
                Program.writer.WriteLine("ROOM_LIST");
        }

        public static void RoomUsers(string room)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_USERS|{room}");
        }
        // ---------- ROOM OPERATIONS ----------
        public static void RoomCreate(string room)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_CREATE|{room}");
        }

        public static void RoomDelete(string room)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_DELETE|{room}");
        }

        public static void RoomJoin(string room)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_JOIN|{room}");
        }

        public static void RoomLeave(string room)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_LEAVE|{room}");
        }

        public static void RoomMessage(string room, string text)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_MSG|{room}|{text}");
        }
        public static void RoomInfo(string room)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_INFO|{room}");
        }
        public static void RoomKick(string room, string user)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_KICK|{room}|{user}");
        }
        public static void RoomRename(string oldName, string newName)
        {
            lock (Program.writerLock)
                Program.writer.WriteLine($"ROOM_RENAME|{oldName}|{newName}");
        }


    }
}