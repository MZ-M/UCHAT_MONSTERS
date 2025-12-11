using System;
using System.IO;
using System.Threading.Tasks;

namespace uchat_client
{
    class SendLoop
    {
        public static void StartSendLoop()
        {
            Console.WriteLine("\nType /help for commands.\n");

            while (true)
            {
                string? input = Console.ReadLine();
                // =========================================
                // FILE DECISION MODE: only /y or /n
                // =========================================
                if (PendingReceive.Awaiting)
                {
                    if (!(input!.Equals("/y", StringComparison.OrdinalIgnoreCase) ||
                          input.Equals("/n", StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.WriteLine("[FILE] You must type: /y   OR   /n");
                        continue;
                    }

                    string fileId = PendingReceive.FileId!;

                    if (input.Equals("/y", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (Program.writerLock)
                            FrameIO.SendText(Program.writer, $"FILE_ACCEPT|{fileId}");

                        Console.WriteLine("[FILE] Accepted.");
                        PendingReceive.Awaiting = false;
                        continue;
                    }

                    if (input.Equals("/n", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (Program.writerLock)
                            FrameIO.SendText(Program.writer, $"FILE_DENY|{fileId}");

                        Console.WriteLine("[FILE] Denied.");
                        PendingReceive.Reset();
                        continue;
                    }
                }

                Console.WriteLine($"RAW INPUT: [{input}]");


                if (string.IsNullOrWhiteSpace(input))
                    continue;

                
                if (!Program.isConnected)
                {
                    Console.WriteLine("[OFFLINE]");
                    continue;
                }

                // ---------------------------------------------------
                // HELP
                // ---------------------------------------------------
                if (Help.IsHelp(input))
                {
                    Help.ShowHelp();
                    continue;
                }



                // ---------------------------------------------------
                // CHANGE CHAT MODE
                // ---------------------------------------------------
                if (input.Equals("/all", StringComparison.OrdinalIgnoreCase))
                {
                    Program.chatMode = "all";
                    Console.WriteLine("[MODE] General chat");
                    continue;
                }

                if (input.StartsWith("/pm ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2);
                    Program.chatMode = "pm:" + args[1];
                    Console.WriteLine("[PM MODE] → " + args[1]);
                    continue;
                }

                if (input.StartsWith("/room ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2);
                    Program.chatMode = "room:" + args[1];
                    Console.WriteLine("[ROOM MODE] → " + args[1]);
                    continue;
                }

                // ---------------------------------------------------
                // REQUEST HISTORY
                // ---------------------------------------------------
                if (input.Equals("/lh", StringComparison.OrdinalIgnoreCase))
                {
                    Commands.RequestHistory();
                    continue;
                }

                // ---------------------------------------------------
                // CLEAR SCREEN
                // ---------------------------------------------------
                if (input.Equals("/cl", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Clear();
                    continue;
                }

                // ---------------------------------------------------
                // ROOM ACTIONS
                // ---------------------------------------------------
                if (input.StartsWith("/roomcreate ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2);
                    Commands.RoomCreate(args[1]);
                    continue;
                }

                if (input.StartsWith("/roomdelete ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2);
                    Commands.RoomDelete(args[1]);
                    continue;
                }

                if (input.StartsWith("/roomjoin ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2);
                    Commands.RoomJoin(args[1]);
                    continue;
                }

                if (input.StartsWith("/roomleave ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2);
                    Commands.RoomLeave(args[1]);
                    continue;
                }

                if (input.StartsWith("/roommsg ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 3);
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: /roommsg <room> <text>");
                        continue;
                    }

                    Commands.RoomMessage(args[1], args[2]);
                    continue;
                }

                // ---------------------------------------------------
                // REQUEST ALL ROOMS
                // ---------------------------------------------------
                if (input.Equals("/rooms", StringComparison.OrdinalIgnoreCase)
                        || input.Equals("/roomlist", StringComparison.OrdinalIgnoreCase))
                {
                    Commands.RequestRoomList();
                    continue;
                }
                // ---------------------------------------------------
                // ROOM USERS LIST
                // ---------------------------------------------------
                if (input.StartsWith("/roomwho ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: /roomwho <room>");
                        continue;
                    }

                    Commands.RoomUsers(args[1]);
                    continue;
                }

                // ---------------------------------------------------
                // ROOM INFO
                // ---------------------------------------------------
                if (input.StartsWith("/roominfo ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: /roominfo <room>");
                        continue;
                    }

                    Commands.RoomInfo(args[1]);
                    continue;
                }

                // ---------------------------------------------------
                // ROOM KICK
                // ---------------------------------------------------
                if (input.StartsWith("/roomkick ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: /roomkick <room> <user>");
                        continue;
                    }

                    Commands.RoomKick(args[1], args[2]);
                    continue;
                }

                // ---------------------------------------------------
                // ROOM RENAME
                // ---------------------------------------------------
                if (input.StartsWith("/roomrename ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: /roomrename <oldName> <newName>");
                        continue;
                    }

                    Commands.RoomRename(args[1], args[2]);
                    continue;
                }

                // ---------------------------------------------------
                // FILE SEND
                // ---------------------------------------------------
                if (input.StartsWith("/file ", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"RAW INPUT: [{input}]");

                    string[] parts = input.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length < 3)
                    {
                        Console.WriteLine("Usage: /file <user|all> <path>");
                        continue;
                    }

                    string target = parts[1];
                    string rawPath = parts[2];

                    // Remove optional quotes
                    string path = rawPath.Trim('"');

                    if (!File.Exists(path))
                    {
                        Console.WriteLine($"File not found: {path}");
                        continue;
                    }

                    Commands.SendFile(target, path);
                    continue;
                }

                // ---------------------------------------------------
                // FILE RECEIVE ACCEPT / DENY
                // ---------------------------------------------------
                if (input.Equals("/accept", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PendingReceive.Awaiting)
                    {
                        Console.WriteLine("[FILE] No incoming file to accept.");
                        continue;
                    }
                                      

                    PendingReceive.Reset();
                    continue;
                }

                if (input.Equals("/deny", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PendingReceive.Awaiting)
                    {
                        Console.WriteLine("[FILE] No incoming file to deny.");
                        continue;
                    }

                    Console.WriteLine("[FILE] File declined.");
                    PendingReceive.Reset();

                    continue;
                }


                // ---------------------------------------------------
                // EDIT MESSAGE
                // ---------------------------------------------------
                if (input.StartsWith("/edit ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 3);
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: /edit <id> <text>");
                        continue;
                    }

                    Commands.EditMessage(args[1], args[2]);
                    continue;
                }

                // ---------------------------------------------------
                // DELETE MESSAGE
                // ---------------------------------------------------
                if (input.StartsWith("/del ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = input.Split(' ', 2);
                    Commands.DeleteMessage(args[1]);
                    continue;
                }

                // ---------------------------------------------------
                // SIMPLE TEXT MESSAGE (depends on mode)
                // ---------------------------------------------------
                if (Program.chatMode == "all")
                {
                    Commands.SendPublic(input);
                }
                else if (Program.chatMode.StartsWith("pm:"))
                {
                    string user = Program.chatMode.Substring(3);
                    Commands.SendPrivate(user, input);
                }
                else if (Program.chatMode.StartsWith("room:"))
                {
                    string room = Program.chatMode.Substring(5);
                    Commands.RoomMessage(room, input);
                }
            }
        }

    }
}