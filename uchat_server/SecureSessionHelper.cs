using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace uchat_server
{
    public static class SecureSessionHelper
    {
        private const string CertificateFileName = "/Users/andrii/Downloads/uchat 1.4/1/uchat12/server.pfx";
        private const string CertificatePassword = "uChatServer#TLS-2025";
        
        private static readonly X509Certificate2? serverCertificate;

        static SecureSessionHelper()
        {
            try
            {
                string certificatePath = Path.Combine(AppContext.BaseDirectory, CertificateFileName);
                serverCertificate = new X509Certificate2(certificatePath, CertificatePassword);
                Console.WriteLine($"[SSL] Certificate loaded successfully: {serverCertificate.Subject}");
                Console.WriteLine("[SSL] Server now requires TLS connection.");
            }
            catch (System.IO.FileNotFoundException)
            {
                Console.WriteLine($"[SSL ERROR] Certificate file not found in application directory");
                Console.WriteLine("[SSL WARNING] Server will not use TLS.");
                serverCertificate = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SSL ERROR] Error loading certificate: {ex.Message}");
                Console.WriteLine("[SSL WARNING] Server will not use TLS.");
                serverCertificate = null;
            }
        }
        public static async Task<SslStream?> AuthenticateSession(TcpClient client)
        {
            if (serverCertificate == null)
            {
                client.Close();
                return null;
            }

            var sslStream = new SslStream(
                client.GetStream(),
                false
            );

            try
            {
                await sslStream.AuthenticateAsServerAsync(serverCertificate);
                
                if (sslStream.IsAuthenticated && sslStream.IsEncrypted)
                {
                    return sslStream;
                }
                else
                {
                    sslStream.Close();
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SSL ERROR] TLS handshake failed for client: {ex.Message}");
                sslStream.Close();
                return null;
            }
        }
    }
}
