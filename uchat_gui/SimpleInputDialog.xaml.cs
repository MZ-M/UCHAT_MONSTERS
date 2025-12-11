using System.Windows;
using System.Windows.Input;

namespace uchat_gui
{
    public partial class SimpleInputDialog : Window
    {
        public string InputText { get; private set; } = "";
        public string DialogTitle { get; set; }
        public string PromptText { get; set; }

        public SimpleInputDialog(string title, string prompt)
        {
            InitializeComponent();
            DialogTitle = title;
            PromptText = prompt;
            DataContext = this;
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
            }
        }
    }
}

