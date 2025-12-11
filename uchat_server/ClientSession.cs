using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace uchat_server
{
    public class ClientSession
    {
        private readonly TcpClient client;

        public BinaryReader Reader { get; }
        public BinaryWriter Writer { get; }

        public string? Username { get; private set; }
        public bool IsAuthenticated => Username != null;

        // ==========================
        //   Store-and-Forward файл
        // ==========================
        public StoredFile? ActiveUpload { get; set; } 
        public StoredFile? ActiveDownload { get; set; } 

        public ClientSession(TcpClient client)
        {
            this.client = client;
            var stream = client.GetStream();

            Reader = new BinaryReader(stream, Encoding.UTF8);
            Writer = new BinaryWriter(stream, Encoding.UTF8);
        }

        public Task Send(string msg)
        {
            FrameIO.SendText(Writer, msg);
            return Task.CompletedTask;
        }

        public void Authenticate(string user)
        {
            Username = user;
        }

        public void Close()
        {
            try { client.Close(); } catch { }
        }
    }
}