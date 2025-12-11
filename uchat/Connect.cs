using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace uchat_client
{
    public class ConnectService
    {
        private static bool ValidateServerCertificate(
              object sender,
              X509Certificate? certificate,
              X509Chain? chain,
              SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Console.WriteLine($"[TLS WARNING] Server certificate errors: {sslPolicyErrors}");
            
            // !!! ВАЖЛИВО: для тестування ми дозволяємо самопідписані сертифікати.
            // У продакшені це слід змінити на сувору перевірку.
            return true; 
        }
        
        public static async Task<bool> Connect(bool loginOrRegister)
        {
            try
            {
                Program.client = new TcpClient();
                await Program.client.ConnectAsync(Program.serverIp, Program.port);
                
                // ===============================================================
                // TLS HANDSHAKE LOGIC
                // ===============================================================
                
                // 1. Створюємо SslStream, використовуючи наш колбек для перевірки сертифіката
                var sslStream = new SslStream(
                    Program.client.GetStream(),
                    false, // Не закривати базовий потік при закритті SslStream
                    ValidateServerCertificate // Наш метод перевірки
                );

                Console.WriteLine("[TLS] Starting handshake...");
                
                // 2. Виконуємо TLS-рукопожаття як клієнт
                // Program.serverIp має співпадати з Common Name у PFX-сертифікаті сервера (наприклад, "localhost")
                await sslStream.AuthenticateAsClientAsync(Program.serverIp); 

                if (!sslStream.IsAuthenticated || !sslStream.IsEncrypted)
                {
                    Console.WriteLine("[TLS ERROR] Handshake failed or connection is not encrypted.");
                    sslStream.Close();
                    Program.client.Close();
                    return false;
                }
                
                Console.WriteLine("[TLS] Handshake successful. Connection is secure.");
                Console.WriteLine($"[TLS INFO] Cipher Algorithm: {sslStream.CipherAlgorithm}");
                Console.WriteLine($"[TLS INFO] Hash Algorithm: {sslStream.HashAlgorithm}");
                Console.WriteLine($"[TLS INFO] Protocol: {sslStream.SslProtocol}");
// ...
                
                // 3. Ініціалізуємо StreamReader та StreamWriter на основі SslStream
                Program.reader = new StreamReader(sslStream, new UTF8Encoding(false));
                Program.writer = new StreamWriter(sslStream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                
                // ===============================================================
                // END TLS LOGIC
                // ===============================================================

                Console.WriteLine("[CONNECTED]");

                // ----------------------------
                // AUTHENTICATION
                // ----------------------------
                if (loginOrRegister)
                {
                    Console.Write("[L]ogin or [R]egister: ");
                    string? mode = Console.ReadLine()?.Trim().ToUpper();
                    string authMode = (mode == "R") ? "REGISTER" : "LOGIN";

                    Console.Write("Username: ");
                    Program.savedLogin = Console.ReadLine()!;

                    Console.Write("Password: ");
                    Program.savedPass = Console.ReadLine()!;

                    await Program.writer.WriteLineAsync(
                        $"AUTH|{authMode}|{Program.savedLogin}|{Program.savedPass}");
                }
                else
                {
                    // Reconnect logic uses saved credentials
                    await Program.writer.WriteLineAsync(
                        $"AUTH|LOGIN|{Program.savedLogin}|{Program.savedPass}");
                }


                var resp = await Program.reader.ReadLineAsync();

                if (resp != "AUTH|OK")
                {
                    Console.WriteLine(resp);
                    return false;
                }

                Console.WriteLine("[AUTH OK]");

                Program.isConnected = true;

                // ------------------------------------------------------
                // START RECEIVING LOOP (NEW RECEIVER CLASS)
                // ------------------------------------------------------
                Receiver.Start();

                return true;
            }
            catch (Exception ex)
            {
                // Якщо помилка при підключенні або рукопожатті, вона буде тут
                Console.WriteLine("[CONNECT ERROR] " + ex.Message);
                
                // Забезпечуємо очищення
                try { Program.client?.Close(); } catch { }
                
                return false;
            }
        }
    }

    // ================================================================
    // RECONNECT LOGIC
    // ================================================================
    class Reconnect
    {
        public static async void StartReconnectLoop()
        {
            while (!Program.isConnected)
            {
                try
                {
                    Console.WriteLine("[RECONNECTING...]");

                    if (await ConnectService.Connect(loginOrRegister: false))
                    {
                        Console.WriteLine("[RECONNECTED]");
                        return;
                    }
                }
                catch
                {
                    // ignore
                }

                await Task.Delay(3000);
            }
        }
    }
}
