using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace uchat_gui
{
    public partial class MessageBubble : UserControl
    {
        public static readonly DependencyProperty SenderNameProperty =
            DependencyProperty.Register(nameof(SenderName), typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnSenderNameChanged));

        public static readonly DependencyProperty MessageTextProperty =
            DependencyProperty.Register(nameof(MessageText), typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnMessageTextChanged));

        public static readonly DependencyProperty TimestampProperty =
            DependencyProperty.Register(nameof(Timestamp), typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnTimestampChanged));

        public static readonly DependencyProperty IsOwnMessageProperty =
            DependencyProperty.Register(nameof(IsOwnMessage), typeof(bool), typeof(MessageBubble),
                new PropertyMetadata(false, OnIsOwnMessageChanged));

        public static readonly DependencyProperty MessageIdProperty =
            DependencyProperty.Register(nameof(MessageId), typeof(long), typeof(MessageBubble));

        public static readonly DependencyProperty IsFileMessageProperty =
            DependencyProperty.Register(nameof(IsFileMessage), typeof(bool), typeof(MessageBubble),
                new PropertyMetadata(false, OnIsFileMessageChanged));

        public static readonly DependencyProperty FilePathProperty =
            DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(MessageBubble));

        public string SenderName
        {
            get => (string)GetValue(SenderNameProperty);
            set => SetValue(SenderNameProperty, value);
        }

        public string MessageText
        {
            get => (string)GetValue(MessageTextProperty);
            set => SetValue(MessageTextProperty, value);
        }

        public string Timestamp
        {
            get => (string)GetValue(TimestampProperty);
            set => SetValue(TimestampProperty, value);
        }

        public bool IsOwnMessage
        {
            get => (bool)GetValue(IsOwnMessageProperty);
            set => SetValue(IsOwnMessageProperty, value);
        }

        public long MessageId
        {
            get => (long)GetValue(MessageIdProperty);
            set => SetValue(MessageIdProperty, value);
        }

        public bool IsFileMessage
        {
            get => (bool)GetValue(IsFileMessageProperty);
            set => SetValue(IsFileMessageProperty, value);
        }

        public string? FilePath
        {
            get => (string?)GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        public event RoutedEventHandler? EditClicked;
        public event RoutedEventHandler? DeleteClicked;
        public event RoutedEventHandler? OpenFileClicked;

        public MessageBubble()
        {
            InitializeComponent();
            Loaded += MessageBubble_Loaded;
        }

        private void MessageBubble_Loaded(object sender, RoutedEventArgs e)
        {
            // Устанавливаем значения напрямую из DependencyProperty
            if (!string.IsNullOrEmpty(SenderName))
                SenderNameText.Text = SenderName;
            if (!string.IsNullOrEmpty(MessageText))
                MessageTextBlock.Text = MessageText;
            if (!string.IsNullOrEmpty(Timestamp))
                TimestampText.Text = Timestamp;
            
            UpdateVisuals();
        }

        private static void OnIsOwnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MessageBubble bubble)
            {
                bubble.UpdateVisuals();
            }
        }

        private static void OnSenderNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MessageBubble bubble && bubble.SenderNameText != null)
            {
                bubble.SenderNameText.Text = e.NewValue?.ToString() ?? "";
            }
        }

        private static void OnMessageTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MessageBubble bubble && bubble.MessageTextBlock != null)
            {
                bubble.MessageTextBlock.Text = e.NewValue?.ToString() ?? "";
            }
        }

        private static void OnTimestampChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MessageBubble bubble && bubble.TimestampText != null)
            {
                bubble.TimestampText.Text = e.NewValue?.ToString() ?? "";
            }
        }

        private static void OnIsFileMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MessageBubble bubble)
            {
                bubble.UpdateFileButtonVisibility();
            }
        }

        private void UpdateTexts()
        {
            if (SenderNameText != null)
                SenderNameText.Text = SenderName ?? "";
            if (MessageTextBlock != null)
                MessageTextBlock.Text = MessageText ?? "";
            if (TimestampText != null)
                TimestampText.Text = Timestamp ?? "";
        }

        private void UpdateVisuals()
        {
            if (IsOwnMessage)
            {
                MessageBorder.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)); // Blue
                MessageBorder.HorizontalAlignment = HorizontalAlignment.Right;
                SenderNameText.Foreground = Brushes.White;
                MessageTextBlock.Foreground = Brushes.White;
                TimestampText.Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                ActionButtonsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBorder.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // Light gray
                MessageBorder.HorizontalAlignment = HorizontalAlignment.Left;
                SenderNameText.Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                MessageTextBlock.Foreground = Brushes.Black;
                TimestampText.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                ActionButtonsPanel.Visibility = Visibility.Collapsed;
            }
            
            UpdateFileButtonVisibility();
        }

        private void UpdateFileButtonVisibility()
        {
            if (OpenFileButton != null)
            {
                OpenFileButton.Visibility = IsFileMessage && !string.IsNullOrEmpty(FilePath) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            EditClicked?.Invoke(this, e);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteClicked?.Invoke(this, e);
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileClicked?.Invoke(this, e);
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            if (e.Property == SenderNameProperty || 
                e.Property == MessageTextProperty || 
                e.Property == TimestampProperty)
            {
                UpdateTexts();
            }
            
            if (e.Property == IsOwnMessageProperty)
            {
                UpdateVisuals();
            }
        }
    }
}

