using System;
using System.IO;
using System.Net.Sockets;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;

namespace uchat_server
{
    public class ClientSession
    {
        private readonly TcpClient client;

        private StreamReader? reader;  
        private StreamWriter? writer; 
        private Stream? baseStream;

        public string? Username { get; private set; }
        public bool IsAuthenticated => Username != null;

        public ClientSession(TcpClient client)
        {
            this.client = client;
            this.baseStream = client.GetStream();
        }
        public async Task<bool> InitializeAsync()
        {
            var sslStream = await SecureSessionHelper.AuthenticateSession(client);
            
            if (sslStream == null)
            {
                return false;
            }
            
            baseStream = sslStream;
            
            reader = new StreamReader(baseStream, new UTF8Encoding(false));
            writer = new StreamWriter(baseStream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            
            return true;
        }

        public Task<string?> ReadLine() => reader?.ReadLineAsync() ?? Task.FromResult<string?>(null);
        
        public Task Send(string msg) => writer?.WriteLineAsync(msg) ?? Task.CompletedTask;

        public void Authenticate(string user)
        {
            Username = user;
        }

        public Stream RawStream => baseStream!; 

        public void Close()
        {
            try 
            {
                baseStream?.Close(); 
            } 
            catch { }
        }
    }
}
