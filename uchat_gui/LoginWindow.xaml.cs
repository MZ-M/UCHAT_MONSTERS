using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace uchat_gui;

public partial class LoginWindow : Window
{
    TcpClient client;
    BinaryReader reader;
    BinaryWriter writer;

    public LoginWindow()
    {
        InitializeComponent();
        LoginBox.Focus();
    }

    async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn)
        {
            btn.IsEnabled = false;
        }
        try
        {
            await Auth("LOGIN");
        }
        finally
        {
            if (sender is System.Windows.Controls.Button button)
            {
                button.IsEnabled = true;
            }
        }
    }

    async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn)
        {
            btn.IsEnabled = false;
        }
        try
        {
            await Auth("REGISTER");
        }
        finally
        {
            if (sender is System.Windows.Controls.Button button)
            {
                button.IsEnabled = true;
            }
        }
    }

    private void LoginBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PasswordBox.Focus();
        }
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Login_Click(sender, e);
        }
    }

    async Task Auth(string mode)
    {
        if (string.IsNullOrWhiteSpace(LoginBox.Text))
        {
            ShowError("Please enter a username.");
            return;
        }

        if (string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            ShowError("Please enter a password.");
            return;
        }

        try
        {
            StatusScrollViewer.Visibility = Visibility.Collapsed;
            StatusText.Text = "Connecting to server...";
            StatusScrollViewer.Visibility = Visibility.Visible;
            
            client = new TcpClient();
            await client.ConnectAsync(ServerConfig.GetServerIp(), ServerConfig.GetServerPort());

            var stream = client.GetStream();

            reader = new BinaryReader(stream, Encoding.UTF8);
            writer = new BinaryWriter(stream, Encoding.UTF8);

            StatusText.Text = "Authenticating...";
            FrameIO.SendText(writer, $"AUTH|{mode}|{LoginBox.Text}|{PasswordBox.Password}");

            var respFrame = FrameIO.ReadFrame(reader);
            if (respFrame == null || respFrame.Type != FrameType.Text)
            {
                ShowError($"No response from server. Make sure the server is running on {ServerConfig.GetServerIp()}:{ServerConfig.GetServerPort()}.");
                try { client?.Close(); } catch { }
                return;
            }

            string resp = Encoding.UTF8.GetString(respFrame.Payload);

            if (resp == "AUTH|OK")
            {
                var chat = new MainWindow(client, LoginBox.Text, PasswordBox.Password);
                chat.Show();
                Close();
            }
            else
            {
                ShowError(resp ?? "Authentication failed");
                try { client?.Close(); } catch { }
            }
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            ShowError($"Cannot connect to server: {ex.Message}\n\nMake sure the server is running:\n  cd uchat_server\n  dotnet run -- {ServerConfig.GetServerPort()}");
            try { client?.Close(); } catch { }
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
            try { client?.Close(); } catch { }
        }
    }

    private void ShowError(string message)
    {
        // Убираем префикс AUTH|FAIL| если он есть
        if (message.StartsWith("AUTH|FAIL|"))
        {
            message = message.Substring("AUTH|FAIL|".Length);
        }
        
        StatusText.Text = message;
        StatusScrollViewer.Visibility = Visibility.Visible;
    }
}
