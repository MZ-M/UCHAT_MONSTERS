using System;
using System.IO;
using System.Threading.Tasks;

namespace uchat_client
{
    public static class PendingFileSend
    {
        public static string? FileId;
        public static string? FilePath;
        public static string? FileName;
        public static long FileSize;
        public static string? Target;
        public static bool IsWaiting = false;
        public static bool IsSending = false;

        public static void Reset()
        {
            FileId = null;
            FilePath = null;
            FileName = null;
            FileSize = 0;
            Target = null;
            IsWaiting = false;
            IsSending = false;
        }

        public static void StartSending()
        {
            if (!IsWaiting || FilePath == null || FileSize <= 0)
            {
                Console.WriteLine("[FILE] No pending file to send.");
                return;
            }

            IsWaiting = false;
            IsSending = true;

            Task.Run(() =>
            {
                try
                {
                    using var fs = File.OpenRead(FilePath);
                    byte[] buffer = new byte[8192];
                    long remaining = FileSize;

                    while (remaining > 0)
                    {
                        int read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read <= 0) break;

                        byte[] chunk = new byte[read];
                        Array.Copy(buffer, chunk, read);

                        lock (Program.writerLock)
                        {
                            FrameIO.WriteFrame(Program.writer,
                                new Frame(FrameType.FileChunk, chunk));
                        }

                        remaining -= read;
                    }

                    Console.WriteLine("[FILE] Upload finished.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[FILE ERROR] " + ex.Message);
                }
                finally
                {
                    Reset();
                }
            });
        }
    }
}