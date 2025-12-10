namespace Client
{
    class Massage
    {
        public static void ProcessMessage(string msg)
        {
            try
            {
                if (msg.StartsWith("USERS|"))
                {
                    Console.WriteLine("[ONLINE] " + msg.Substring(6));
                    return;
                }

                if (msg.StartsWith("FILE|"))
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
                    return;
                }

                if (msg.StartsWith("MSG|"))
                {
                    var p = msg.Split('|', 6);

                    long id = long.Parse(p[1]);
                    string time = p[2];
                    string sender = p[3];
                    string receiver = p[4];
                    string text = p[5];

                    Console.WriteLine(
                        $"[{id}] {time} | {sender} -> {receiver}: {text}");
                    return;
                }

                if (msg.StartsWith("ERROR|"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] " + msg.Substring(6));
                    Console.ResetColor();
                    return;
                }

                Console.WriteLine(msg);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CLIENT ERROR] " + ex.Message);
            }
        }
    }
}