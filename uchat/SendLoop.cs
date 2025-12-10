namespace Client
{
    class SendMessage
    {
        public static async void StartSendLoop()
        {
            Console.WriteLine("\nType /help for commands.\n");

            while (true)
            {
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (!Program.isConnected)
                {
                    Console.WriteLine("[OFFLINE]");
                    continue;
                }

                if (Help.IsHelp(input))
                {
                    Help.ShowHelp();
                    continue;
                }

                if (input.StartsWith("/pm ", StringComparison.OrdinalIgnoreCase))
                {
                    var p = input.Split(' ', 3);
                    if (p.Length < 3)
                    {
                        Console.WriteLine("Usage: /pm <user> <message>");
                        continue;
                    }

                    await Program.writer.WriteLineAsync($"MSG|{p[1]}|{p[2]}");
                    continue;
                }

                if (input.StartsWith("/file ", StringComparison.OrdinalIgnoreCase))
                {
                    var p = input.Split(' ', 3);
                    if (p.Length < 3)
                    {
                        Console.WriteLine("Usage: /file <user|all> <path>");
                        continue;
                    }

                    if (!File.Exists(p[2]))
                    {
                        Console.WriteLine("File not found.");
                        continue;
                    }

                    string fn = Path.GetFileName(p[2]);
                    long size = new FileInfo(p[2]).Length;

                    await Program.writer.WriteLineAsync(
                        $"FILE|{p[1]}|{fn}|{size}");

                    using var fs = File.OpenRead(p[2]);
                    await fs.CopyToAsync(Program.writer.BaseStream);

                    Console.WriteLine($"[FILE SENT] {fn}");
                    continue;
                }

                if (input.StartsWith("/edit ", StringComparison.OrdinalIgnoreCase))
                {
                    var p = input.Split(' ', 3);
                    if (p.Length < 3)
                    {
                        Console.WriteLine("Usage: /edit <id> <text>");
                        continue;
                    }

                    await Program.writer.WriteLineAsync($"EDIT|{p[1]}|{p[2]}");
                    continue;
                }

                if (input.StartsWith("/del ", StringComparison.OrdinalIgnoreCase))
                {
                    var p = input.Split(' ', 2);
                    if (p.Length < 2)
                    {
                        Console.WriteLine("Usage: /del <id>");
                        continue;
                    }

                    await Program.writer.WriteLineAsync($"DEL|{p[1]}");
                    continue;
                }

                if (input.Equals("/lh", StringComparison.OrdinalIgnoreCase))
                {
                    await Program.writer.WriteLineAsync("HISTORY");
                    continue;
                }

                if (input.Equals("/cl", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Clear();
                    continue;
                }

                await Program.writer.WriteLineAsync($"MSG|all|{input}");
            }
        }
    }
}