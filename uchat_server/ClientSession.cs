using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace uchat_server
{
    public class ClientSession
    {
        private readonly TcpClient client;
        private readonly StreamReader reader;
        private readonly StreamWriter writer;

        public string? Username { get; private set; }
        public bool IsAuthenticated => Username != null;

        public ClientSession(TcpClient client)
        {
            this.client = client;

            var stream = client.GetStream();

            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        public Task<string?> ReadLine() => reader.ReadLineAsync();
        public Task Send(string msg) => writer.WriteLineAsync(msg);

        public void Authenticate(string user)
        {
            Username = user;
        }

        public NetworkStream RawStream =>
            (NetworkStream)reader.BaseStream;

        public void Close()
        {
            try { client.Close(); } catch { }
        }
    }
}
