using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace uchat_gui
{
    public partial class MainWindow : Window
    {
        bool connected = true;
        string savedPassword = "";
        string serverIp = ServerConfig.GetServerIp();
        int serverPort = ServerConfig.GetServerPort();

        TcpClient client;
        BinaryReader reader;
        BinaryWriter writer;
        readonly object writerLock = new object();

        string currentUser = "";
        ChatItem? selectedMessageForEdit = null;
        ObservableCollection<ChatItemViewModel> chatItems = new(); // Отображаемые сообщения (отфильтрованные)
        List<ChatItemViewModel> allMessages = new(); // Все сообщения (без фильтрации)
        HashSet<string> userRooms = new(); // Комнаты, в которых состоит пользователь

        // File sending state
        string? pendingFilePath;
        string? pendingFileName;
        long pendingFileSize;
        string? pendingFileTarget;
        bool isWaitingForFileReady = false;
        bool isSendingFile = false;

        // File receiving state
        bool isReceivingFile = false;
        long fileBytesRemaining = 0;
        string? currentReceivingFileName;
        FileStream? receivingFileStream;

        // ===== constructor =====
        public MainWindow(TcpClient connectedClient, string login, string password)
        {
            InitializeComponent();

            currentUser = login;
            savedPassword = password;

            Title = $"uChat - {currentUser}";
            CurrentUserText.Text = currentUser;

            client = connectedClient;

            InitStreams();

            // Bind chat items collection
            ChatItemsControl.ItemsSource = chatItems;

            // Initialize selected user display
            UpdateSelectedUserDisplay();

            // request chat history
            lock (writerLock)
            {
                FrameIO.SendText(writer, "HISTORY|PUBLIC");
                // Запрашиваем список комнат
                FrameIO.SendText(writer, "ROOM_LIST");
            }

            Task.Run(ReadLoop);
        }

        void InitStreams()
        {
            var stream = client.GetStream();

            reader = new BinaryReader(stream, Encoding.UTF8);
            writer = new BinaryWriter(stream, Encoding.UTF8);
        }

        void AllUsers_Click(object sender, RoutedEventArgs e)
        {
            UsersList.SelectedIndex = -1;
            UpdateSelectedUserDisplay();
            FilterMessagesBySelectedUser();
        }

        void CreateRoom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SimpleInputDialog("Create Room", "Enter room name:");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                lock (writerLock)
                {
                    FrameIO.SendText(writer, $"ROOM_CREATE|{dialog.InputText.Trim()}");
                }
            }
        }

        void JoinRoom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SimpleInputDialog("Join Room", "Enter room name to join:");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                lock (writerLock)
                {
                    FrameIO.SendText(writer, $"ROOM_JOIN|{dialog.InputText.Trim()}");
                }
            }
        }

        private void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UsersList.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected) && selected.StartsWith("#"))
            {
                // Это комната - проверяем, состоит ли пользователь в ней
                string roomName = selected.Substring(1);
                if (!userRooms.Contains(roomName))
                {
                    // Пользователь не в комнате - отменяем выбор
                    MessageBox.Show($"You are not a member of room '{roomName}'. Please join the room first.", 
                        "Not a Member", MessageBoxButton.OK, MessageBoxImage.Warning);
                    UsersList.SelectedItem = null;
                    UpdateSelectedUserDisplay();
                    FilterMessagesBySelectedUser();
                    return;
                }
            }
            
            UpdateSelectedUserDisplay();
            FilterMessagesBySelectedUser();
            
            // Запрашиваем историю для выбранного пользователя или комнаты
            if (connected && writer != null)
            {
                lock (writerLock)
                {
                    if (UsersList.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
                    {
                        if (selected.StartsWith("#"))
                        {
                            // Это комната
                            string roomName = selected.Substring(1);
                            FrameIO.SendText(writer, $"HISTORY|ROOM|{roomName}");
                        }
                        else
                        {
                            // Это пользователь
                            FrameIO.SendText(writer, $"HISTORY|PM|{selected}");
                        }
                    }
                    else
                    {
                        // Публичный чат
                        FrameIO.SendText(writer, "HISTORY|PUBLIC");
                    }
                }
            }
        }

        private void UpdateSelectedUserDisplay()
        {
            if (UsersList.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                if (selected.StartsWith("#"))
                {
                    // Это комната
                    string roomName = selected.Substring(1);
                    SelectedUserText.Text = $"Room: {roomName}";
                }
                else
                {
                    // Это пользователь
                    SelectedUserText.Text = $"Chat with: {selected}";
                }
                SelectedUserText.Visibility = Visibility.Visible;
            }
            else
            {
                SelectedUserText.Text = "Public Chat";
                SelectedUserText.Visibility = Visibility.Visible;
            }
        }

        private void AddMessageToChat(ChatItem item)
        {
            var viewModel = new ChatItemViewModel(item, currentUser);
            allMessages.Add(viewModel);
            FilterMessagesBySelectedUser();
            ScrollChatToEnd();
        }

        private void FilterMessagesBySelectedUser()
        {
            string? selectedUser = UsersList.SelectedItem as string;
            
            // Очищаем отображаемые сообщения
            chatItems.Clear();
            
            // Фильтруем сообщения по выбранному пользователю или комнате
            if (!string.IsNullOrWhiteSpace(selectedUser))
            {
                if (selectedUser.StartsWith("#"))
                {
                    // Это комната
                    string roomName = selectedUser.Substring(1);
                    var filtered = allMessages.Where(vm =>
                    {
                        var msg = vm.Item;
                        // Сообщения в комнату (receiver = roomName, без префикса #)
                        bool isRoomMessage = msg.Receiver.Equals(roomName, StringComparison.OrdinalIgnoreCase);
                        // Или системные сообщения о файлах в этой комнате
                        bool isSystemFileMessage = msg.Sender == "System" && 
                                                  (msg.Text.Contains(roomName) || msg.Receiver.Equals(roomName, StringComparison.OrdinalIgnoreCase));
                        return isRoomMessage || isSystemFileMessage;
                    }).ToList();
                    
                    foreach (var item in filtered)
                    {
                        chatItems.Add(item);
                    }
                }
                else
                {
                    // Приватный чат: показываем только сообщения между currentUser и selectedUser
                    var filtered = allMessages.Where(vm =>
                    {
                        var msg = vm.Item;
                        // Сообщение от currentUser к selectedUser или от selectedUser к currentUser
                        bool isPrivateChat = (msg.Sender.Equals(currentUser, StringComparison.OrdinalIgnoreCase) && 
                                             msg.Receiver.Equals(selectedUser, StringComparison.OrdinalIgnoreCase)) ||
                                            (msg.Sender.Equals(selectedUser, StringComparison.OrdinalIgnoreCase) && 
                                             msg.Receiver.Equals(currentUser, StringComparison.OrdinalIgnoreCase));
                        // Или системные сообщения о файлах в этом чате
                        bool isSystemFileMessage = msg.Sender == "System" && 
                                                  (msg.Text.Contains(selectedUser) || msg.Receiver.Equals(selectedUser, StringComparison.OrdinalIgnoreCase));
                        return isPrivateChat || isSystemFileMessage;
                    }).ToList();
                    
                    foreach (var item in filtered)
                    {
                        chatItems.Add(item);
                    }
                }
            }
            else
            {
                // Публичный чат: показываем только сообщения с receiver = "all" или "ALL"
                var filtered = allMessages.Where(vm =>
                {
                    var msg = vm.Item;
                    return msg.Receiver.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                           msg.Receiver.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
                           msg.Sender == "System"; // Системные сообщения
                }).ToList();
                
                foreach (var item in filtered)
                {
                    chatItems.Add(item);
                }
            }
            
            ScrollChatToEnd();
        }

        // ===== SAFE AUTO-SCROLL =====
        void ScrollChatToEnd()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (chatItems.Count > 0)
                {
                    ChatScrollViewer.ScrollToEnd();
                }
            }), DispatcherPriority.Background);
        }

        // ===== RECEIVE LOOP =====
        async Task ReadLoop()
        {
            while (true)
            {
                try
                {
                    var frame = FrameIO.ReadFrame(reader);
                    if (frame == null)
                        break;

                    if (frame.Type == FrameType.Text)
                    {
                        string msg = Encoding.UTF8.GetString(frame.Payload);
                        await ProcessTextMessage(msg);
                    }
                    else if (frame.Type == FrameType.FileChunk)
                    {
                        ProcessFileChunk(frame.Payload);
                    }
                }
                catch
                {
                    connected = false;

                    Dispatcher.Invoke(() =>
                    {
                        var item = new ChatItem
                        {
                            Id = 0,
                            Text = "[SERVER DISCONNECTED]",
                            RawText = "[SERVER DISCONNECTED]",
                            Sender = "System",
                            Receiver = currentUser,
                            Time = DateTime.Now.ToString("HH:mm"),
                            IsFileMessage = false
                        };
                        AddMessageToChat(item);
                        UpdateConnectionStatus(false);
                    });

                    // Clean up file receiving
                    if (isReceivingFile)
                    {
                        receivingFileStream?.Close();
                        receivingFileStream = null;
                        isReceivingFile = false;
                        fileBytesRemaining = 0;
                    }

                    StartReconnectLoop();
                    break;
                }
            }
        }

        async Task ProcessTextMessage(string msg)
        {
            // ==== HISTORY reload ====
            if (msg == "HISTORY")
            {
                Dispatcher.Invoke(() =>
                {
                    // Очищаем только сообщения с ID > 0 (реальные сообщения из истории)
                    // Системные сообщения (ID = 0) оставляем
                    var systemMessages = allMessages.Where(vm => vm.Item.Id == 0).ToList();
                    var realMessages = allMessages.Where(vm => vm.Item.Id > 0).ToList();
                    
                    // Удаляем только реальные сообщения из allMessages
                    foreach (var msgItem in realMessages)
                    {
                        allMessages.Remove(msgItem);
                    }
                });

                lock (writerLock)
                {
                    string target = UsersList.SelectedItem is string u && !string.IsNullOrWhiteSpace(u)
                        ? $"HISTORY|PM|{u}"
                        : "HISTORY|PUBLIC";
                    FrameIO.SendText(writer, target);
                }
                return;
            }

            // ==== END of HISTORY marker ====
            if (msg == "--END--")
            {
                return;
            }

            // ==== HISTORY_UPDATED (после редактирования/удаления) ====
            if (msg == "HISTORY_UPDATED")
            {
                // Не показываем это как сообщение
                // История будет обновлена через запрос HISTORY, который мы отправили после EDIT
                return;
            }

            // ==== ROOM_LIST (список всех комнат) ====
            if (msg.StartsWith("ROOM_LIST|"))
            {
                var p = msg.Substring("ROOM_LIST|".Length);
                if (p == "NONE")
                {
                    return;
                }

                var rooms = p.Split(',');
                Dispatcher.Invoke(() =>
                {
                    // Добавляем комнаты в список (с префиксом #)
                    // Но показываем только те, в которых пользователь состоит
                    foreach (var roomInfo in rooms)
                    {
                        if (string.IsNullOrWhiteSpace(roomInfo))
                            continue;

                        // Формат: RoomName(Owner)
                        var roomName = roomInfo.Contains('(') 
                            ? roomInfo.Substring(0, roomInfo.IndexOf('(')).Trim()
                            : roomInfo.Trim();
                        
                        if (!string.IsNullOrWhiteSpace(roomName))
                        {
                            // Проверяем, состоит ли пользователь в комнате
                            // Пока добавляем все комнаты, но при выборе будем проверять
                            string roomDisplay = $"#{roomName}";
                            // Добавляем только если еще нет в списке
                            if (!UsersList.Items.Cast<object>().Any(item => item.ToString() == roomDisplay))
                            {
                                UsersList.Items.Add(roomDisplay);
                            }
                        }
                    }
                });
                return;
            }

            // ==== ROOM_USERS (список участников комнаты) ====
            if (msg.StartsWith("ROOM_USERS|"))
            {
                var parts = msg.Split('|');
                if (parts.Length >= 3)
                {
                    string roomName = parts[1];
                    string membersList = parts[2];
                    
                    Dispatcher.Invoke(() =>
                    {
                        if (membersList == "NONE")
                        {
                            MessageBox.Show($"Room '{roomName}' has no members.", "Room Members", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            var members = membersList.Split(',');
                            string membersText = string.Join("\n", members);
                            MessageBox.Show($"Members of room '{roomName}':\n\n{membersText}", "Room Members", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                return;
            }

            // ==== ROOM_RENAME|OK (комната переименована) ====
            if (msg.StartsWith("ROOM_RENAME|OK|"))
            {
                var parts = msg.Split('|');
                if (parts.Length >= 4)
                {
                    string oldName = parts[2];
                    string newName = parts[3];
                    Dispatcher.Invoke(() =>
                    {
                        string oldDisplay = $"#{oldName}";
                        string newDisplay = $"#{newName}";
                        
                        // Обновляем в списке
                        int index = UsersList.Items.IndexOf(oldDisplay);
                        if (index >= 0)
                        {
                            UsersList.Items[index] = newDisplay;
                        }
                        
                        // Обновляем в userRooms
                        userRooms.Remove(oldName);
                        userRooms.Add(newName);
                        
                        // Если комната была выбрана, обновляем выбор
                        if (UsersList.SelectedItem?.ToString() == oldDisplay)
                        {
                            UsersList.SelectedItem = newDisplay;
                        }
                        
                        MessageBox.Show($"Room renamed from '{oldName}' to '{newName}'", "Room Renamed", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                return;
            }

            // ==== ROOM_RENAME|RENAMED (комната была переименована другим пользователем) ====
            if (msg.StartsWith("ROOM_RENAME|RENAMED|"))
            {
                var parts = msg.Split('|');
                if (parts.Length >= 4)
                {
                    string oldName = parts[2];
                    string newName = parts[3];
                    Dispatcher.Invoke(() =>
                    {
                        string oldDisplay = $"#{oldName}";
                        string newDisplay = $"#{newName}";
                        
                        // Обновляем в списке
                        int index = UsersList.Items.IndexOf(oldDisplay);
                        if (index >= 0)
                        {
                            UsersList.Items[index] = newDisplay;
                        }
                        
                        // Обновляем в userRooms
                        userRooms.Remove(oldName);
                        userRooms.Add(newName);
                        
                        // Если комната была выбрана, обновляем выбор
                        if (UsersList.SelectedItem?.ToString() == oldDisplay)
                        {
                            UsersList.SelectedItem = newDisplay;
                        }
                    });
                }
                return;
            }

            // ==== ROOM_KICK|OK (пользователь удален из комнаты) ====
            if (msg.StartsWith("ROOM_KICK|OK|"))
            {
                var parts = msg.Split('|');
                if (parts.Length >= 4)
                {
                    string roomName = parts[2];
                    string kickedUser = parts[3];
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"User '{kickedUser}' has been removed from room '{roomName}'", "User Kicked", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                return;
            }

            // ==== ROOM_KICK|KICKED (вас удалили из комнаты) ====
            if (msg.StartsWith("ROOM_KICK|KICKED|"))
            {
                var roomName = msg.Substring("ROOM_KICK|KICKED|".Length);
                Dispatcher.Invoke(() =>
                {
                    string roomDisplay = $"#{roomName}";
                    userRooms.Remove(roomName);
                    UsersList.Items.Remove(roomDisplay);
                    
                    // Если эта комната была выбрана, переключаемся на публичный чат
                    if (UsersList.SelectedItem?.ToString() == roomDisplay)
                    {
                        UsersList.SelectedItem = null;
                        UpdateSelectedUserDisplay();
                        FilterMessagesBySelectedUser();
                    }
                    
                    MessageBox.Show($"You have been removed from room '{roomName}'", "Kicked from Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }

            // ==== ROOM|CREATED (комната создана) ====
            if (msg.StartsWith("ROOM|CREATED|"))
            {
                var roomName = msg.Substring("ROOM|CREATED|".Length);
                Dispatcher.Invoke(() =>
                {
                    string roomDisplay = $"#{roomName}";
                    userRooms.Add(roomName);
                    if (!UsersList.Items.Cast<object>().Any(item => item.ToString() == roomDisplay))
                    {
                        UsersList.Items.Add(roomDisplay);
                    }
                    MessageBox.Show($"Room '{roomName}' created successfully!", "Room Created", MessageBoxButton.OK, MessageBoxImage.Information);
                });
                return;
            }

            // ==== ROOM|JOINED (присоединились к комнате) ====
            if (msg.StartsWith("ROOM|JOINED|"))
            {
                var roomName = msg.Substring("ROOM|JOINED|".Length);
                Dispatcher.Invoke(() =>
                {
                    string roomDisplay = $"#{roomName}";
                    if (!UsersList.Items.Cast<object>().Any(item => item.ToString() == roomDisplay))
                    {
                        UsersList.Items.Add(roomDisplay);
                    }
                    // Запрашиваем историю комнаты
                    lock (writerLock)
                    {
                        FrameIO.SendText(writer, $"HISTORY|ROOM|{roomName}");
                    }
                });
                return;
            }

            // ==== ROOM|LEFT (покинули комнату) ====
            if (msg.StartsWith("ROOM|LEFT|"))
            {
                var roomName = msg.Substring("ROOM|LEFT|".Length);
                Dispatcher.Invoke(() =>
                {
                    string roomDisplay = $"#{roomName}";
                    userRooms.Remove(roomName);
                    UsersList.Items.Remove(roomDisplay);
                    // Если эта комната была выбрана, переключаемся на публичный чат
                    if (UsersList.SelectedItem?.ToString() == roomDisplay)
                    {
                        UsersList.SelectedItem = null;
                        UpdateSelectedUserDisplay();
                        FilterMessagesBySelectedUser();
                    }
                });
                return;
            }

            // ==== ROOM|DELETED (комната удалена) ====
            if (msg.StartsWith("ROOM|DELETED|"))
            {
                var roomName = msg.Substring("ROOM|DELETED|".Length);
                Dispatcher.Invoke(() =>
                {
                    string roomDisplay = $"#{roomName}";
                    UsersList.Items.Remove(roomDisplay);
                    // Если эта комната была выбрана, переключаемся на публичный чат
                    if (UsersList.SelectedItem?.ToString() == roomDisplay)
                    {
                        UsersList.SelectedItem = null;
                        UpdateSelectedUserDisplay();
                        FilterMessagesBySelectedUser();
                    }
                });
                return;
            }

            // ==== ROOM_UPDATE|CREATED (другая комната была создана) ====
            if (msg.StartsWith("ROOM_UPDATE|CREATED|"))
            {
                var parts = msg.Split('|');
                if (parts.Length >= 3)
                {
                    var roomName = parts[2];
                    Dispatcher.Invoke(() =>
                    {
                        string roomDisplay = $"#{roomName}";
                        if (!UsersList.Items.Cast<object>().Any(item => item.ToString() == roomDisplay))
                        {
                            UsersList.Items.Add(roomDisplay);
                        }
                    });
                }
                return;
            }

            // ==== ROOM|EXISTS (комната уже существует) ====
            if (msg.StartsWith("ROOM|EXISTS|"))
            {
                var roomName = msg.Substring("ROOM|EXISTS|".Length);
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Room '{roomName}' already exists!", "Room Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }

            // ==== ROOM|ALREADY (уже в комнате) ====
            if (msg.StartsWith("ROOM|ALREADY|"))
            {
                var roomName = msg.Substring("ROOM|ALREADY|".Length);
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"You are already a member of room '{roomName}'!", "Already Member", MessageBoxButton.OK, MessageBoxImage.Information);
                });
                return;
            }

            // ==== ROOM|NOT_MEMBER (не член комнаты) ====
            if (msg.StartsWith("ROOM|NOT_MEMBER|"))
            {
                var roomName = msg.Substring("ROOM|NOT_MEMBER|".Length);
                Dispatcher.Invoke(() =>
                {
                    userRooms.Remove(roomName);
                    string roomDisplay = $"#{roomName}";
                    UsersList.Items.Remove(roomDisplay);
                    MessageBox.Show($"You are not a member of room '{roomName}'!", "Not a Member", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }

            // ==== ERROR|NotOwner (не владелец комнаты) ====
            if (msg == "ERROR|NotOwner")
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Only the room owner can perform this action.", "Not Owner", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }

            // ==== ERROR|OwnerCannotBeKicked (владелец не может быть удален) ====
            if (msg == "ERROR|OwnerCannotBeKicked")
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("The room owner cannot be kicked from the room.", "Cannot Kick Owner", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }

            // ==== ERROR|UserNotInRoom (пользователь не в комнате) ====
            if (msg == "ERROR|UserNotInRoom")
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("This user is not a member of the room.", "User Not in Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }

            // ==== USERS LIST ====
            if (msg.StartsWith("USERS|"))
            {
                var users = msg.Substring(6).Split(",");

                Dispatcher.Invoke(() =>
                {
                    // Сохраняем комнаты (элементы, начинающиеся с #)
                    var rooms = UsersList.Items.Cast<object>()
                        .Where(item => item.ToString()?.StartsWith("#") == true)
                        .ToList();

                    UsersList.Items.Clear();

                    // Восстанавливаем комнаты
                    foreach (var room in rooms)
                    {
                        UsersList.Items.Add(room);
                    }

                    // Добавляем пользователей
                    foreach (var u in users)
                    {
                        if (string.IsNullOrWhiteSpace(u))
                            continue;

                        if (u == currentUser)
                            continue;

                        UsersList.Items.Add(u);
                    }
                });
                return;
            }

            // ==== FILE_OFFER (новый файл от другого пользователя) ====
            if (msg.StartsWith("FILE_OFFER|"))
            {
                var p = msg.Split('|');
                string fileId = p[1];
                string sender = p[2];
                string fileName = p[3];
                long size = long.Parse(p[4]);

                // Автоматически принимаем файл
                lock (writerLock)
                {
                    FrameIO.SendText(writer, $"FILE_ACCEPT|{fileId}");
                }

                Dispatcher.Invoke(() =>
                {
                    var item = new ChatItem
                    {
                        Id = 0,
                        Text = $"📎 Receiving file from {sender}: {fileName} ({FormatFileSize(size)})",
                        RawText = $"FILE_OFFER from {sender}: {fileName}",
                        Sender = sender,
                        Receiver = currentUser,
                        Time = DateTime.Now.ToString("HH:mm"),
                        IsFileMessage = true
                    };
                    AddMessageToChat(item);
                });
                return;
            }

            // ==== FILE_BEGIN (сервер начинает отправлять файл) ====
            if (msg.StartsWith("FILE_BEGIN|"))
            {
                var p = msg.Split('|');
                string fileName = p[1];
                long size = long.Parse(p[2]);

                StartReceivingFile(fileName, size);
                return;
            }

            // ==== FILE_DONE (файл полностью получен) ====
            if (msg.StartsWith("FILE_DONE|"))
            {
                FinishReceivingFile();
                return;
            }

            // ==== FILE_UPLOAD_READY (сервер готов принять файл) ====
            if (msg.StartsWith("FILE_UPLOAD_READY|"))
            {
                var p = msg.Split('|');
                string fileId = p[1];
                // fileId не используется, но сохраняем для совместимости
                StartSendingFile();
                return;
            }

            // ==== FILE_STORED (файл сохранен на сервере) ====
            if (msg.StartsWith("FILE_STORED|"))
            {
                // Файл успешно отправлен и сохранен на сервере
                // Обновляем сообщение о файле, если оно есть
                Dispatcher.Invoke(() =>
                {
                    // Ищем последнее сообщение о файле от текущего пользователя
                    var fileMessage = chatItems
                        .Where(vm => vm.Item.Sender == currentUser && vm.Item.IsFileMessage && 
                                     vm.Item.Text.Contains("Sending file"))
                        .LastOrDefault();
                    
                    if (fileMessage != null && pendingFileName != null)
                    {
                        // Обновляем существующее сообщение
                        fileMessage.Item.Text = $"✅ File sent to {pendingFileTarget}: {pendingFileName}";
                        fileMessage.Item.RawText = $"FILE SENT: {pendingFileName}";
                        
                        // Обновляем MessageBubble
                        var container = ChatItemsControl.ItemContainerGenerator.ContainerFromItem(fileMessage);
                        if (container != null)
                        {
                            var bubble = FindVisualChild<MessageBubble>(container);
                            if (bubble != null)
                            {
                                bubble.MessageText = fileMessage.Item.Text;
                            }
                        }
                    }
                    else
                    {
                        // Добавляем новое сообщение, если не нашли существующее
                        var item = new ChatItem
                        {
                            Id = 0,
                            Text = $"✅ File sent successfully: {pendingFileName ?? "file"}",
                            RawText = "FILE_STORED",
                            Sender = "System",
                            Receiver = currentUser,
                            Time = DateTime.Now.ToString("HH:mm"),
                            IsFileMessage = true
                        };
                        AddMessageToChat(item);
                    }
                });
                return;
            }

            // ==== FILE_DENIED ====
            if (msg.StartsWith("FILE_DENIED|"))
            {
                var p = msg.Split('|');
                string who = p[1];
                Dispatcher.Invoke(() =>
                {
                    var item = new ChatItem
                    {
                        Id = 0,
                        Text = $"❌ {who} denied your file",
                        RawText = $"FILE_DENIED by {who}",
                        Sender = "System",
                        Receiver = currentUser,
                        Time = DateTime.Now.ToString("HH:mm"),
                        IsFileMessage = true
                    };
                    AddMessageToChat(item);
                });
                ResetFileSending();
                return;
            }

            // ==== ERROR MESSAGES ====
            if (msg.StartsWith("ERROR|"))
            {
                string errorText = msg.Substring("ERROR|".Length);
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(errorText, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return;
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
                {
                    // Проверяем, есть ли уже сообщение с таким ID (для обновления при редактировании)
                    // Ищем в allMessages, так как там хранятся все сообщения
                    var existingViewModel = allMessages.FirstOrDefault(vm => vm.Item.Id == id);
                    
                    if (existingViewModel != null)
                    {
                        // Обновляем существующее сообщение в allMessages
                        // Убираем " (edited)" если сервер добавил его
                        string displayText = text;
                        if (displayText.EndsWith(" (edited)"))
                        {
                            displayText = displayText.Substring(0, displayText.Length - " (edited)".Length);
                        }
                        
                        existingViewModel.Item.Text = displayText;
                        existingViewModel.Item.RawText = $"{time} | {sender} -> {receiver}: {displayText}";
                        existingViewModel.Item.Time = time;
                        existingViewModel.Item.Sender = sender; // Обновляем на случай изменения
                        existingViewModel.Item.Receiver = receiver; // Обновляем на случай изменения
                        
                        // Обновляем MessageBubble, если сообщение отображается в текущем чате
                        var displayedViewModel = chatItems.FirstOrDefault(vm => vm.Item.Id == id);
                        if (displayedViewModel != null)
                        {
                            // Обновляем текст в отображаемом сообщении
                            displayedViewModel.Item.Text = displayText;
                            displayedViewModel.Item.RawText = $"{time} | {sender} -> {receiver}: {displayText}";
                            displayedViewModel.Item.Time = time;
                            
                            var container = ChatItemsControl.ItemContainerGenerator.ContainerFromItem(displayedViewModel);
                            if (container != null)
                            {
                                var bubble = FindVisualChild<MessageBubble>(container);
                                if (bubble != null)
                                {
                                    bubble.MessageText = displayText;
                                    bubble.Timestamp = time;
                                }
                            }
                        }
                        
                        // Применяем фильтрацию, чтобы обновленное сообщение отобразилось в правильном чате
                        FilterMessagesBySelectedUser();
                    }
                    else
                    {
                        // Добавляем новое сообщение через AddMessageToChat, чтобы оно попало в allMessages
                        // Убираем " (edited)" если сервер добавил его
                        string displayText = text;
                        if (displayText.EndsWith(" (edited)"))
                        {
                            displayText = displayText.Substring(0, displayText.Length - " (edited)".Length);
                        }
                        
                        var item = new ChatItem
                        {
                            Id = id,
                            Text = displayText,
                            RawText = $"{time} | {sender} -> {receiver}: {displayText}",
                            Sender = sender,
                            Receiver = receiver,
                            Time = time,
                            IsFileMessage = false
                        };
                        AddMessageToChat(item);
                    }
                });
                return;
            }

            // ==== FALLBACK (только для неизвестных сообщений, не ERROR) ====
            // Игнорируем неизвестные сообщения, чтобы они не попадали в чат
        }

        void ProcessFileChunk(byte[] chunk)
        {
            if (!isReceivingFile || receivingFileStream == null)
            {
                // Игнорируем чанк, если не ожидаем файл
                return;
            }

            try
            {
                receivingFileStream.Write(chunk, 0, chunk.Length);
                fileBytesRemaining -= chunk.Length;

                if (fileBytesRemaining <= 0)
                {
                    FinishReceivingFile();
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var item = new ChatItem
                    {
                        Id = 0,
                        Text = $"❌ File receive error: {ex.Message}",
                        RawText = $"FILE_ERROR: {ex.Message}",
                        Sender = "System",
                        Receiver = currentUser,
                        Time = DateTime.Now.ToString("HH:mm"),
                        IsFileMessage = true
                    };
                    AddMessageToChat(item);
                });
                receivingFileStream?.Close();
                receivingFileStream = null;
                isReceivingFile = false;
                fileBytesRemaining = 0;
            }
        }

        void StartReceivingFile(string fileName, long size)
        {
            try
            {
                string dir = Path.Combine(Environment.CurrentDirectory, "downloads");
                Directory.CreateDirectory(dir);

                string savePath = Path.Combine(dir, fileName);
                receivingFileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write);
                fileBytesRemaining = size;
                currentReceivingFileName = savePath;
                isReceivingFile = true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var item = new ChatItem
                    {
                        Id = 0,
                        Text = $"❌ Cannot start receiving file: {ex.Message}",
                        RawText = $"FILE_START_ERROR: {ex.Message}",
                        Sender = "System",
                        Receiver = currentUser,
                        Time = DateTime.Now.ToString("HH:mm"),
                        IsFileMessage = true
                    };
                    AddMessageToChat(item);
                });
                isReceivingFile = false;
            }
        }

        void FinishReceivingFile()
        {
            if (receivingFileStream != null && currentReceivingFileName != null)
            {
                receivingFileStream.Close();
                receivingFileStream = null;

                string fullPath = Path.GetFullPath(currentReceivingFileName);
                string fileName = Path.GetFileName(currentReceivingFileName);
                
                Dispatcher.Invoke(() =>
                {
                    // Ищем существующее сообщение о получении файла (созданное при FILE_OFFER)
                    var existingFileMessage = chatItems
                        .Where(vm => vm.Item.IsFileMessage && 
                                     vm.Item.Text.Contains("Receiving file") &&
                                     vm.Item.Sender != currentUser)
                        .LastOrDefault();
                    
                    if (existingFileMessage != null)
                    {
                        // Обновляем существующее сообщение
                        existingFileMessage.Item.Text = $"✅ File saved: {fileName}\n📁 Location: {fullPath}";
                        existingFileMessage.Item.RawText = $"FILE SAVED TO: {fullPath}";
                        existingFileMessage.Item.FilePath = fullPath;
                        
                        // Обновляем MessageBubble
                        var container = ChatItemsControl.ItemContainerGenerator.ContainerFromItem(existingFileMessage);
                        if (container != null)
                        {
                            var bubble = FindVisualChild<MessageBubble>(container);
                            if (bubble != null)
                            {
                                bubble.MessageText = existingFileMessage.Item.Text;
                                bubble.FilePath = fullPath;
                                bubble.IsFileMessage = true;
                            }
                        }
                    }
                    else
                    {
                        // Создаем новое сообщение, если не нашли существующее
                        var item = new ChatItem
                        {
                            Id = 0,
                            Text = $"✅ File saved: {fileName}\n📁 Location: {fullPath}",
                            RawText = $"FILE SAVED TO: {fullPath}",
                            Sender = "System",
                            Receiver = currentUser,
                            Time = DateTime.Now.ToString("HH:mm"),
                            IsFileMessage = true,
                            FilePath = fullPath // Сохраняем путь к файлу
                        };
                        AddMessageToChat(item);
                    }
                });
            }

            isReceivingFile = false;
            fileBytesRemaining = 0;
            currentReceivingFileName = null;
        }

        private void MessageBubble_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is MessageBubble bubble)
            {
                // Получаем ViewModel из DataContext или Tag
                ChatItemViewModel? viewModel = null;
                
                if (bubble.Tag is ChatItemViewModel vm)
                {
                    viewModel = vm;
                }
                else if (bubble.DataContext is ChatItemViewModel dc)
                {
                    viewModel = dc;
                }
                else
                {
                    // Пытаемся найти по MessageId
                    var item = chatItems.FirstOrDefault(i => i.Id == bubble.MessageId);
                    if (item != null)
                        viewModel = item;
                }

                if (viewModel != null)
                {
                    bubble.Tag = viewModel;
                    bubble.EditClicked += (s, args) =>
                    {
                        HandleEditMessage(viewModel.Item);
                    };
                    bubble.DeleteClicked += (s, args) =>
                    {
                        HandleDeleteMessage(viewModel.Item);
                    };
                    bubble.OpenFileClicked += (s, args) =>
                    {
                        HandleOpenFile(viewModel.Item);
                    };
                    
                    // Устанавливаем свойства файла
                    bubble.IsFileMessage = viewModel.Item.IsFileMessage;
                    bubble.FilePath = viewModel.Item.FilePath;
                }
            }
        }

        private void HandleEditMessage(ChatItem item)
        {
            if (item.Sender.Equals(currentUser, StringComparison.OrdinalIgnoreCase) && item.Id > 0)
            {
                selectedMessageForEdit = item;
                // Убираем " (edited)" из текста, если он там есть
                string textToEdit = item.Text;
                if (textToEdit.EndsWith(" (edited)"))
                {
                    textToEdit = textToEdit.Substring(0, textToEdit.Length - " (edited)".Length);
                }
                InputBox.Text = textToEdit;
                InputBox.Focus();
                InputBox.SelectAll();
                EditModeIndicator.Visibility = Visibility.Visible;
                EditDeletePanel.Visibility = Visibility.Visible;
                SendButton.Content = "Update";
            }
        }

        private void HandleOpenFile(ChatItem item)
        {
            if (string.IsNullOrEmpty(item.FilePath))
                return;

            try
            {
                if (File.Exists(item.FilePath))
                {
                    // Открываем папку с файлом и выделяем файл
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{item.FilePath}\""
                    });
                }
                else
                {
                    // Если файл не найден, открываем папку downloads
                    string downloadsDir = Path.Combine(Environment.CurrentDirectory, "downloads");
                    if (Directory.Exists(downloadsDir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = downloadsDir,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        MessageBox.Show($"File not found:\n{item.FilePath}\n\nDownloads folder does not exist.", 
                            "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot open file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleDeleteMessage(ChatItem item)
        {
            if (item.Sender.Equals(currentUser, StringComparison.OrdinalIgnoreCase) && item.Id > 0)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to delete this message?",
                    "Delete Message",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    DeleteMessage(item);
                }
            }
        }

        // ===== SEND TEXT =====
        void Send_Click(object sender, RoutedEventArgs e)
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

                // Check if we're in edit mode
                if (selectedMessageForEdit != null)
                {
                    long messageId = selectedMessageForEdit.Id;
                    
                    lock (writerLock)
                    {
                        FrameIO.SendText(writer, $"EDIT|{messageId}|{text}");
                    }
                    
                    // Сбрасываем режим редактирования
                    selectedMessageForEdit = null;
                    EditModeIndicator.Visibility = Visibility.Collapsed;
                    EditDeletePanel.Visibility = Visibility.Collapsed;
                    SendButton.Content = "Send";
                    
                    // Запрашиваем обновленную историю для получения отредактированного сообщения
                    Task.Delay(100).ContinueWith(_ =>
                    {
                        lock (writerLock)
                        {
                            string target = UsersList.SelectedItem is string u && !string.IsNullOrWhiteSpace(u)
                                ? $"HISTORY|PM|{u}"
                                : "HISTORY|PUBLIC";
                            FrameIO.SendText(writer, target);
                        }
                    });
                }
                else
                {
                    string target;
                    if (UsersList.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
                    {
                        if (selected.StartsWith("#"))
                        {
                            // Это комната - отправляем ROOM_MSG
                            string roomName = selected.Substring(1);
                            lock (writerLock)
                            {
                                FrameIO.SendText(writer, $"ROOM_MSG|{roomName}|{text}");
                            }
                        }
                        else
                        {
                            // Это пользователь - отправляем MSG
                            target = selected;
                            lock (writerLock)
                            {
                                FrameIO.SendText(writer, $"MSG|{target}|{text}");
                            }
                        }
                    }
                    else
                    {
                        // Публичный чат
                        target = "all";
                        lock (writerLock)
                        {
                            FrameIO.SendText(writer, $"MSG|{target}|{text}");
                        }
                    }
                }

                InputBox.Clear();
                InputBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Send error");
            }
        }

        // ===== SEND FILE =====
        void SendFile_Click(object sender, RoutedEventArgs e)
        {
            if (!connected)
            {
                MessageBox.Show("Server connection lost. Reconnecting...");
                return;
            }

            if (isWaitingForFileReady || isSendingFile)
            {
                MessageBox.Show("File transfer already in progress.");
                return;
            }

            try
            {
                // Get recipient (file transfer works for users and rooms, not public chat)
                if (UsersList.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected))
                {
                    MessageBox.Show("Please select a user or room to send the file to.\n\nFile transfer is not available for public chat.", 
                        "Select Recipient", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string receiver = selected; // Может быть пользователь или комната (#roomName)

                var dlg = new Microsoft.Win32.OpenFileDialog();
                if (dlg.ShowDialog() != true)
                    return;

                string path = dlg.FileName;
                string filename = Path.GetFileName(path);
                long size = new FileInfo(path).Length;

                // Сохраняем информацию о файле
                pendingFilePath = path;
                pendingFileName = filename;
                pendingFileSize = size;
                pendingFileTarget = receiver;
                isWaitingForFileReady = true;

                // Отправляем запрос на отправку файла
                lock (writerLock)
                {
                    FrameIO.SendText(writer, $"FILE|{receiver}|{filename}|{size}");
                }

                Dispatcher.Invoke(() =>
                {
                    var item = new ChatItem
                    {
                        Id = 0,
                        Text = $"📤 Sending file to {receiver}: {filename} ({FormatFileSize(size)})",
                        RawText = $"SENDING FILE to {receiver}: {filename}",
                        Sender = currentUser,
                        Receiver = receiver,
                        Time = DateTime.Now.ToString("HH:mm"),
                        IsFileMessage = true
                    };
                    AddMessageToChat(item);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "File send error");
                ResetFileSending();
            }
        }

        void StartSendingFile()
        {
            if (pendingFilePath == null || !File.Exists(pendingFilePath))
            {
                ResetFileSending();
                return;
            }

            isWaitingForFileReady = false;
            isSendingFile = true;

            Task.Run(() =>
            {
                try
                {
                    using var fs = File.OpenRead(pendingFilePath);
                    byte[] buffer = new byte[8192];
                    long remaining = pendingFileSize;

                    while (remaining > 0)
                    {
                        int read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read <= 0) break;

                        byte[] chunk = new byte[read];
                        Array.Copy(buffer, chunk, read);

                        lock (writerLock)
                        {
                            if (writer == null || !connected)
                            {
                                throw new Exception("Connection lost during file transfer");
                            }
                            FrameIO.WriteFrame(writer, new Frame(FrameType.FileChunk, chunk));
                        }

                        remaining -= read;
                    }

                    // Файл отправлен, ждем подтверждения FILE_STORED от сервера
                    // Сообщение об успешной отправке будет обновлено при получении FILE_STORED
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Обновляем сообщение о файле на ошибку
                        var fileMessage = chatItems
                            .Where(vm => vm.Item.Sender == currentUser && vm.Item.IsFileMessage && 
                                         vm.Item.Text.Contains("Sending file"))
                            .LastOrDefault();
                        
                        if (fileMessage != null && pendingFileName != null)
                        {
                            fileMessage.Item.Text = $"❌ File upload failed: {ex.Message}";
                            fileMessage.Item.RawText = $"FILE_UPLOAD_FAILED: {pendingFileName}";
                            
                            var container = ChatItemsControl.ItemContainerGenerator.ContainerFromItem(fileMessage);
                            if (container != null)
                            {
                                var bubble = FindVisualChild<MessageBubble>(container);
                                if (bubble != null)
                                {
                                    bubble.MessageText = fileMessage.Item.Text;
                                }
                            }
                        }
                        else
                        {
                            var item = new ChatItem
                            {
                                Id = 0,
                                Text = $"❌ File upload failed: {ex.Message}",
                                RawText = "FILE_UPLOAD_FAILED",
                                Sender = "System",
                                Receiver = currentUser,
                                Time = DateTime.Now.ToString("HH:mm"),
                                IsFileMessage = true
                            };
                            AddMessageToChat(item);
                        }
                    });
                    ResetFileSending();
                }
                finally
                {
                    ResetFileSending();
                }
            });
        }

        void ResetFileSending()
        {
            isWaitingForFileReady = false;
            isSendingFile = false;
            pendingFilePath = null;
            pendingFileName = null;
            pendingFileSize = 0;
            pendingFileTarget = null;
        }

        // ===== EDIT =====
        async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (selectedMessageForEdit != null)
            {
                HandleEditMessage(selectedMessageForEdit);
            }
            else
            {
                MessageBox.Show("Select a message to edit first.");
            }
        }

        // ===== DELETE =====
        async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (selectedMessageForEdit != null)
            {
                HandleDeleteMessage(selectedMessageForEdit);
            }
            else
            {
                MessageBox.Show("Select a message to delete first.");
            }
        }

        private void DeleteMessage(ChatItem item)
        {
            if (!connected)
            {
                MessageBox.Show("Server connection lost. Reconnecting...");
                return;
            }

            try
            {
                lock (writerLock)
                {
                    FrameIO.SendText(writer, $"DEL|{item.Id}");
                }
                
                // Remove from UI
                Dispatcher.Invoke(() =>
                {
                    var viewModel = chatItems.FirstOrDefault(vm => vm.Item == item);
                    if (viewModel != null)
                    {
                        chatItems.Remove(viewModel);
                    }
                    if (selectedMessageForEdit == item)
                    {
                        selectedMessageForEdit = null;
                        EditModeIndicator.Visibility = Visibility.Collapsed;
                        EditDeletePanel.Visibility = Visibility.Collapsed;
                        SendButton.Content = "Send";
                        InputBox.Clear();
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Delete error");
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                Send_Click(sender, e);
            }
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            if (isConnected)
            {
                StatusIndicator.Fill = new SolidColorBrush(Colors.Green);
                StatusText.Text = "Connected";
                AttachFileButton.IsEnabled = true;
                SendButton.IsEnabled = true;
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                StatusText.Text = "Disconnected";
                AttachFileButton.IsEnabled = false;
                SendButton.IsEnabled = false;
            }
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
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                        StatusText.Text = "Reconnecting...";
                    });

                    var tcp = new TcpClient();
                    await tcp.ConnectAsync(serverIp, serverPort);

                    client = tcp;

                    InitStreams();

                    // авто-логин
                    lock (writerLock)
                    {
                        FrameIO.SendText(writer, $"AUTH|LOGIN|{currentUser}|{savedPassword}");
                    }

                    var respFrame = FrameIO.ReadFrame(reader);
                    if (respFrame == null || respFrame.Type != FrameType.Text)
                        throw new Exception("No response from server");

                    string resp = Encoding.UTF8.GetString(respFrame.Payload);
                    if (resp != "AUTH|OK")
                        throw new Exception("Auth failed");

                    connected = true;

                    Dispatcher.Invoke(() =>
                    {
                        var item = new ChatItem
                        {
                            Id = 0,
                            Text = "[RECONNECTED]",
                            RawText = "[RECONNECTED]",
                            Sender = "System",
                            Receiver = currentUser,
                            Time = DateTime.Now.ToString("HH:mm"),
                            IsFileMessage = false
                        };
                        AddMessageToChat(item);
                        UpdateConnectionStatus(true);
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

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        private void UsersList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (UsersList.SelectedItem is string selected && selected.StartsWith("#"))
            {
                // Это комната - показываем меню
                LeaveRoomMenuItem.Visibility = Visibility.Visible;
                DeleteRoomMenuItem.Visibility = Visibility.Visible;
            }
            else
            {
                LeaveRoomMenuItem.Visibility = Visibility.Collapsed;
                DeleteRoomMenuItem.Visibility = Visibility.Collapsed;
            }
        }

        private void LeaveRoomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selected && selected.StartsWith("#"))
            {
                string roomName = selected.Substring(1);
                lock (writerLock)
                {
                    FrameIO.SendText(writer, $"ROOM_LEAVE|{roomName}");
                }
            }
        }

        private void DeleteRoomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selected && selected.StartsWith("#"))
            {
                string roomName = selected.Substring(1);
                var result = MessageBox.Show(
                    $"Are you sure you want to delete room '{roomName}'?\n\nOnly the room owner can delete it.",
                    "Delete Room",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    lock (writerLock)
                    {
                        FrameIO.SendText(writer, $"ROOM_DELETE|{roomName}");
                    }
                }
            }
        }

        private void RenameRoomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selected && selected.StartsWith("#"))
            {
                string roomName = selected.Substring(1);
                var dialog = new SimpleInputDialog("Rename Room", $"Enter new name for room '{roomName}':");
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    string newName = dialog.InputText.Trim();
                    if (newName != roomName)
                    {
                        lock (writerLock)
                        {
                            FrameIO.SendText(writer, $"ROOM_RENAME|{roomName}|{newName}");
                        }
                    }
                }
            }
        }

        private void ViewMembersMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selected && selected.StartsWith("#"))
            {
                string roomName = selected.Substring(1);
                lock (writerLock)
                {
                    FrameIO.SendText(writer, $"ROOM_USERS|{roomName}");
                }
            }
        }

        private void KickUserMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UsersList.SelectedItem is string selected && selected.StartsWith("#"))
            {
                string roomName = selected.Substring(1);
                var dialog = new SimpleInputDialog("Kick User", $"Enter username to remove from room '{roomName}':");
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    string username = dialog.InputText.Trim();
                    lock (writerLock)
                    {
                        FrameIO.SendText(writer, $"ROOM_KICK|{roomName}|{username}");
                    }
                }
            }
        }
    }

    public class ChatItem
    {
        public long Id { get; set; }

        // Весь формат сообщения:
        public string RawText { get; set; } = "";

        // ТОЛЬКО текст сообщения:
        public string Text { get; set; } = "";

        // Дополнительная информация для GUI
        public string Sender { get; set; } = "";
        public string Receiver { get; set; } = "";
        public string Time { get; set; } = "";
        public bool IsFileMessage { get; set; } = false;
        public string? FileInfo { get; set; }
        public string? FilePath { get; set; } // Полный путь к сохраненному файлу

        public override string ToString() => RawText;
    }

    public class ChatItemViewModel
    {
        public ChatItem Item { get; set; }
        public string Sender => Item.Sender;
        public string Text => Item.Text;
        public string Time => Item.Time;
        public long Id => Item.Id;
        public bool IsOwnMessage { get; set; }

        public ChatItemViewModel(ChatItem item, string currentUser)
        {
            Item = item;
            IsOwnMessage = item.Sender.Equals(currentUser, StringComparison.OrdinalIgnoreCase);
        }
    }
}
