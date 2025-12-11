using System.IO;

namespace uchat_client
{
    public static class PendingReceive
    {
        public static bool Awaiting = false;

        public static string? FileId;
        public static string? Sender;
        public static string? FileName;
        public static long FileSize;

        public static void Set(string fileId, string sender, string fileName, long fileSize)
        {
            Awaiting = true;
            FileId = fileId;
            Sender = sender;
            FileName = fileName;
            FileSize = fileSize;
        }

        public static void Reset()
        {
            Awaiting = false;
            FileId = null;
            Sender = null;
            FileName = null;
            FileSize = 0;
        }
    }
    public static class FileReceiver
    {
        public static bool IsReceiving = false;
        public static long BytesRemaining = 0;
        public static string? CurrentFileName;
        public static FileStream? FS;

        public static void Start(string filename, long size)
        {
            try
            {
                Directory.CreateDirectory(Program.DownloadDir);
                string savePath = Path.Combine(Program.DownloadDir, filename);

                FS = new FileStream(savePath, FileMode.Create, FileAccess.Write);
                BytesRemaining = size;
                CurrentFileName = savePath;
                IsReceiving = true;

                Console.WriteLine($"[FILE] Receiving {filename} ({size} bytes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FILE ERROR] Cannot start receiving: " + ex.Message);
                Reset();
            }
        }

        public static void ProcessChunk(byte[] chunk)
        {
            if (!IsReceiving || FS == null)
            {
                Console.WriteLine("[FILE WARN] Chunk ignored — not receiving.");
                return;
            }

            try
            {
                FS.Write(chunk, 0, chunk.Length);
                BytesRemaining -= chunk.Length;

                if (BytesRemaining <= 0)
                {
                    FS.Close();
                    Console.WriteLine($"[FILE] Saved: {CurrentFileName}");
                    Reset();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FILE ERROR] Write failed: " + ex.Message);
                Reset();
            }
        }

        public static void Reset()
        {
            try { FS?.Close(); } catch { }
            FS = null;
            IsReceiving = false;
            BytesRemaining = 0;
            CurrentFileName = null;
        }
    }
}