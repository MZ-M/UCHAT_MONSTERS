using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Windows;

namespace uchat_gui;

public partial class LoginWindow : Window
{
	TcpClient client;
	StreamReader reader;
	StreamWriter writer;

    public LoginWindow()
    {
        InitializeComponent();
    }

    async void Login_Click(object sender, RoutedEventArgs e)
    {
        await Auth("LOGIN");
    }

    async void Register_Click(object sender, RoutedEventArgs e)
    {
        await Auth("REGISTER");
    }

    async Task Auth(string mode)
    {
        try
        {
            client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 5000);

            var stream = client.GetStream();

            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false)){ AutoFlush=true };

            await writer.WriteLineAsync(
                $"AUTH|{mode}|{LoginBox.Text}|{PasswordBox.Password}");

            var resp = await reader.ReadLineAsync();

            if (resp == "AUTH|OK")
            {
                var chat = new MainWindow(client, LoginBox.Text, PasswordBox.Password);
                chat.Show();
                Close();
            }
            else
            {
                StatusText.Text = resp!;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}