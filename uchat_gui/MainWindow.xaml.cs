using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;

namespace uchat_gui
{
    public partial class MainWindow : Window
    {

        bool connected = true;
        string savedPassword = "";
        string serverIp = "127.0.0.1";
        int serverPort = 5000;

        TcpClient client;
        StreamReader reader;
        StreamWriter writer;

        string currentUser = "";

        // ===== constructor =====
        public MainWindow(TcpClient connectedClient, string login, string password)
                {
            InitializeComponent();

            currentUser = login;
            savedPassword = password;

            Title = $"uChat - {currentUser}";

            client = connectedClient;

            InitStreams();

            // request chat history
            writer.WriteLineAsync("HISTORY");

            Task.Run(ReadLoop);
        }

        void InitStreams()
        {
            var stream = client.GetStream();

            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        void AllUsers_Click(object sender, RoutedEventArgs e)
        {
            UsersList.SelectedIndex = -1;
        }


        // ===== SAFE AUTO-SCROLL =====
        void ScrollChatToEnd()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ChatList.Items.Count > 0)
                    ChatList.ScrollIntoView(ChatList.Items[^1]);
            }), DispatcherPriority.Background);
        }



        // ===== RECEIVE LOOP =====
        async Task ReadLoop()
        {
            while (true)
            {
                try
                {
                    var msg = await reader.ReadLineAsync();
                    if (msg == null)
                        break;

                    // ==== HISTORY reload ====
                    if (msg == "HISTORY")
                    {
                        Dispatcher.Invoke(() =>
                            ChatList.Items.Clear());

                        await writer.WriteLineAsync("HISTORY");
                        continue;
                    }

                    // ==== END of HISTORY marker ====
                    if (msg == "--END--")
                    {
                        continue;
                    }

                    // ==== USERS LIST ====
                    if (msg.StartsWith("USERS|"))
                    {
                        var users = msg.Substring(6).Split(",");

                        Dispatcher.Invoke(() =>
                        {
                            UsersList.Items.Clear();

                            foreach (var u in users)
                            {
                                if (string.IsNullOrWhiteSpace(u))
                                    continue;

                                if (u == currentUser)
                                    continue;

                                UsersList.Items.Add(u);
                            }
                        });

                        continue;
                    }

                    // ==== FILE RECEIVE ====
                    if (msg.StartsWith("FILE|"))
                    {
                        var p = msg.Split('|');

                        string sender = p[1];
                        string filename = p[2];
                        long size = long.Parse(p[3]);

                        Dispatcher.Invoke(() =>
                            ChatList.Items.Add(
                                $"RECEIVING FILE from {sender}: {filename} ({size} bytes)")
                        );
                        ScrollChatToEnd();

                        string dir = Path.Combine(
                            Environment.CurrentDirectory,
                            "downloads");

                        Directory.CreateDirectory(dir);

                        string savePath = Path.Combine(dir, filename);

                        using var fs = new FileStream(
                            savePath,
                            FileMode.Create,
                            FileAccess.Write);

                        byte[] buffer = new byte[8192];
                        long remain = size;

                        while (remain > 0)
                        {
                            int read = await reader.BaseStream.ReadAsync(
                                buffer,
                                0,
                                (int)Math.Min(buffer.Length, remain));

                            if (read <= 0)
                                break;

                            await fs.WriteAsync(buffer, 0, read);
                            remain -= read;
                        }

                        Dispatcher.Invoke(() =>
                            ChatList.Items.Add($"FILE SAVED TO: {savePath}")
                        );
                        ScrollChatToEnd();

                        continue;
                    }




                    // ==== PROTOCOL MSG ====
                    if (msg.StartsWith("MSG|"))
                    {
                        var p = msg.Split('|', 6);

                        long id = long.Parse(p[1]);
                        string time = p[2];
                        string sender = p[3];
                        string receiver = p[4];
                        string text = p[5];

                        Dispatcher.Invoke(() =>
                            ChatList.Items.Add(
                                new ChatItem
                                {
                                    Id = id,
                                    Text = text,
                                    RawText = $"{time} | {sender} -> {receiver}: {text}"
                                })
                        );

                        ScrollChatToEnd();
                        continue;
                    }



                    // ==== FALLBACK ====
                    Dispatcher.Invoke(() =>
                        ChatList.Items.Add(msg)
                    );
                    ScrollChatToEnd();


                }
                catch
                {
                    connected = false;

                    Dispatcher.Invoke(() =>
                    {

                        ChatList.Items.Add("[SERVER DISCONNECTED]");
                        ScrollChatToEnd();
                        Title = $"uChat - {currentUser} (DISCONNECTED)";
                    });

                    StartReconnectLoop();
                    break;
                }
            }
        }



        private void ChatList_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (ChatList.SelectedItem is not ChatItem item)
                return;

            InputBox.Text = item.Text;
            InputBox.Focus();
        }


        // ===== SEND TEXT =====
        async void Send_Click(object sender, RoutedEventArgs e)
        {
            if (!connected)
            {
                MessageBox.Show("Server connection lost. Reconnecting...");
                return;
            }

            if (string.IsNullOrWhiteSpace(InputBox.Text))
                return;

            try
            {
                string text = InputBox.Text.Trim();

                string target =
                    UsersList.SelectedItem is string u && !string.IsNullOrWhiteSpace(u)
                        ? u
                        : "ALL";

                await writer.WriteLineAsync($"MSG|{target}|{text}");

                // УБРАЛИ локальный вывод!
                InputBox.Clear();
                InputBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Send error");
            }
        }


        // ===== SEND FILE =====
        async void SendFile_Click(object sender, RoutedEventArgs e)
        {
            if (!connected)
            {
                MessageBox.Show("Server connection lost. Reconnecting...");
                return;
            }

            try
            {
                // recipient check
                if (UsersList.SelectedItem is not string receiver ||
                    string.IsNullOrWhiteSpace(receiver))
                {
                    MessageBox.Show("Please select recipient first.");
                    return;
                }

                var dlg = new Microsoft.Win32.OpenFileDialog();
                if (dlg.ShowDialog() != true)
                    return;

                string path = dlg.FileName;
                string filename = Path.GetFileName(path);
                long size = new FileInfo(path).Length;

                // send header
                await writer.WriteLineAsync(
                    $"FILE|{receiver}|{filename}|{size}");

                // send bytes
                using var fs = File.OpenRead(path);
                await fs.CopyToAsync(writer.BaseStream);

                Dispatcher.Invoke(() =>
                    ChatList.Items.Add(
                        $"SENT FILE to {receiver}: {filename} ({size} bytes)")
                );
                ScrollChatToEnd();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "File send error");
            }
        }



        // ===== EDIT =====
        async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (!connected)
            {
                MessageBox.Show("Server connection lost. Reconnecting...");
                return;
            }

            if (ChatList.SelectedItem is not ChatItem item)
            {
                MessageBox.Show("Select message first");
                return;
            }

            string text = InputBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(text))
                return;

            await writer.WriteLineAsync($"EDIT|{item.Id}|{text}");

            InputBox.Clear();
        }

        // ===== DELETE =====
        async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!connected)
            {
                MessageBox.Show("Server connection lost. Reconnecting...");
                return;
            }

            if (ChatList.SelectedItem is not ChatItem item)
            {
                MessageBox.Show("Select message first");
                return;
            }

            await writer.WriteLineAsync($"DEL|{item.Id}");
        }

        async void StartReconnectLoop()
        {
            while (!connected)
            {
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        Title = $"uChat - {currentUser} (reconnecting...)";
                    });

                    var tcp = new TcpClient();
                    await tcp.ConnectAsync(serverIp, serverPort);

                    client = tcp;

                    InitStreams();

                    // авто-логин
                    await writer.WriteLineAsync(
                        $"AUTH|LOGIN|{currentUser}|{savedPassword}");

                    var resp = await reader.ReadLineAsync();
                    if (resp != "AUTH|OK")
                        throw new Exception("Auth failed");

                    // загрузка истории
                    //await writer.WriteLineAsync("HISTORY");

                    connected = true;

                    Dispatcher.Invoke(() =>
                    {
                        ChatList.Items.Add("[RECONNECTED]");
                        ScrollChatToEnd();
                        Title = $"uChat - {currentUser}";
                    });

                    Task.Run(ReadLoop);
                    return;
                }
                catch
                {
                    await Task.Delay(3000);
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
    public class ChatItem
    {
        public long Id { get; set; }

        // Весь формат сообщения:
        public string RawText { get; set; } = "";

        // ТОЛЬКО текст сообщения:
        public string Text { get; set; } = "";

        public override string ToString() => RawText;
    }



}