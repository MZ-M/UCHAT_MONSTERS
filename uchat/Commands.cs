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
                FrameIO.SendText(Program.writer, $"MSG|all|{text}");
        }

        // ---------- PRIVATE ----------
        public static void SendPrivate(string user, string text)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"MSG|{user}|{text}");
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
            {
                FrameIO.SendText(Program.writer, $"FILE|{target}|{fn}|{size}");
            }

            Console.WriteLine($"[FILE] Waiting for FILE_READY from server...");
            PendingFileSend.FilePath = path;
            PendingFileSend.FileName = fn;
            PendingFileSend.FileSize = size;
            PendingFileSend.Target = target;
            PendingFileSend.IsWaiting = true;
        }

        // ---------- EDIT ----------
        public static void EditMessage(string id, string text)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"EDIT|{id}|{text}");
        }

        // ---------- DELETE ----------
        public static void DeleteMessage(string id)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"DEL|{id}");
        }

        // ---------- HISTORY ----------
        public static void RequestHistory()
        {
            lock (Program.writerLock)
            {
                if (Program.chatMode == "all")
                {
                    FrameIO.SendText(Program.writer, "HISTORY|PUBLIC");
                }
                else if (Program.chatMode.StartsWith("pm:"))
                {
                    string user = Program.chatMode.Substring(3);
                    FrameIO.SendText(Program.writer, $"HISTORY|PM|{user}");
                }
                else if (Program.chatMode.StartsWith("room:"))
                {
                    string room = Program.chatMode.Substring(5);
                    FrameIO.SendText(Program.writer, $"HISTORY|ROOM|{room}");
                }
            }
        }

        // ---------- ROOMS ----------
        public static void RequestRoomList()
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, "ROOM_LIST");
        }

        public static void RoomUsers(string room)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_USERS|{room}");
        }
        // ---------- ROOM OPERATIONS ----------
        public static void RoomCreate(string room)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_CREATE|{room}");
        }

        public static void RoomDelete(string room)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_DELETE|{room}");
        }

        public static void RoomJoin(string room)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_JOIN|{room}");
        }

        public static void RoomLeave(string room)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_LEAVE|{room}");
        }

        public static void RoomMessage(string room, string text)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_MSG|{room}|{text}");
        }
        public static void RoomInfo(string room)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_INFO|{room}");
        }
        public static void RoomKick(string room, string user)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_KICK|{room}|{user}");
        }
        public static void RoomRename(string oldName, string newName)
        {
            lock (Program.writerLock)
                FrameIO.SendText(Program.writer, $"ROOM_RENAME|{oldName}|{newName}");
        }
    }
}