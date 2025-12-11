using System;
using System.Collections.Generic;

namespace uchat_server
{
    public class StoredFile
    {
        public string FileId { get; set; } = Guid.NewGuid().ToString("N");

        public string Sender { get; set; } = "";

        public string FileName { get; set; } = "";

        public string FilePath { get; set; } = "";

        public long FileSize { get; set; }
        public long BytesUploaded { get; set; }
        public bool UploadComplete => BytesUploaded >= FileSize;

        public bool IsRoomFile { get; set; } = false;

        public string? RoomName { get; set; }

        public List<string> Receivers { get; set; } = new();

        public HashSet<string> Accepted { get; set; } = new();

        public HashSet<string> Denied { get; set; } = new();
    }
}
