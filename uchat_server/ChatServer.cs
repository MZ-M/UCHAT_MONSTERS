using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uchat_server
{
    public static class ChatServer
    {
        public static async Task HandleClient(ClientSession session)
        {
            try
            {
                while (true)
                {
                    var frame = FrameIO.ReadFrame(session.Reader);

                    if (frame == null)
                        break;

                    if (frame.Type == FrameType.Text)
                    {
                        string cmd = Encoding.UTF8.GetString(frame.Payload);

                        Console.WriteLine($"RAW from {session.Username ?? "?"}: {cmd}");

                        await CommandProcessor.ProcessAsync(session, cmd);
                    }
                    else if (frame.Type == FrameType.FileChunk)
                    {
                        // ================================
                        //  STORE-AND-FORWARD: UPLOAD
                        // ================================
                        if (session.ActiveUpload == null)
                        {
                            Console.WriteLine("[WARN] FileChunk without ActiveUpload");
                            continue;
                        }

                        var sf = session.ActiveUpload;
                        var chunk = frame.Payload;

                        try
                        {
                            using (var fs = new FileStream(
                                sf.FilePath,
                                FileMode.Append,
                                FileAccess.Write,
                                FileShare.Read))
                            {
                                fs.Write(chunk, 0, chunk.Length);
                            }

                            sf.BytesUploaded += chunk.Length;

                            if (sf.BytesUploaded >= sf.FileSize)
                            {
                                Console.WriteLine(
                                    $"[FILE] upload complete {sf.FileName} ({sf.FileId})");

                                ServerState.PendingFiles.Add(sf);

                                FrameIO.SendText(session.Writer,
                                    $"FILE_STORED|{sf.FileId}");

                                session.ActiveUpload = null;

                                foreach (var username in sf.Receivers)
                                {
                                    var target = ServerState.clients
                                        .FirstOrDefault(c =>
                                            c.IsAuthenticated &&
                                            c.Username!.Equals(username, StringComparison.OrdinalIgnoreCase));

                                    if (target != null)
                                    {
                                        FrameIO.SendText(
                                            target.Writer,
                                            $"FILE_OFFER|{sf.FileId}|{sf.Sender}|{sf.FileName}|{sf.FileSize}"
                                        );
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[ERR] Upload failed: " + ex.Message);
                            FrameIO.SendText(session.Writer, "ERROR|FILE_UPLOAD_FAILED");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Client error: " + ex.Message);
            }

            if (session.ActiveUpload != null)
            {
                var sf = session.ActiveUpload;
                try { File.Delete(sf.FilePath); } catch { }
                Console.WriteLine($"[CLEANUP] Unfinished upload removed: {sf.FileName}");
            }

            lock (ServerState.clients)
                ServerState.clients.Remove(session);

            ServerState.BroadcastUsers();
            Console.WriteLine($"Client disconnected: {session.Username}");
        }
    }
}