namespace uchat_gui
{
    /// <summary>
    /// Конфигурация подключения к серверу
    /// </summary>
    public static class ServerConfig
    {
        /// <summary>
        /// IP адрес сервера по умолчанию
        /// </summary>
        public static string DefaultServerIp { get; set; } = "127.0.0.1";

        /// <summary>
        /// Порт сервера по умолчанию
        /// </summary>
        public static int DefaultServerPort { get; set; } = 5000;

        /// <summary>
        /// Получить IP адрес сервера (можно переопределить через переменные окружения или настройки)
        /// </summary>
        public static string GetServerIp()
        {
            // Можно добавить чтение из переменных окружения или файла конфигурации
            var envIp = System.Environment.GetEnvironmentVariable("UCHAT_SERVER_IP");
            return !string.IsNullOrWhiteSpace(envIp) ? envIp : DefaultServerIp;
        }

        /// <summary>
        /// Получить порт сервера (можно переопределить через переменные окружения или настройки)
        /// </summary>
        public static int GetServerPort()
        {
            // Можно добавить чтение из переменных окружения или файла конфигурации
            var envPort = System.Environment.GetEnvironmentVariable("UCHAT_SERVER_PORT");
            if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out int port))
            {
                return port;
            }
            return DefaultServerPort;
        }
    }
}

