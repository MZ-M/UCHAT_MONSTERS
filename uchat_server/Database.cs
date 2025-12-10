using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace uchat_server
{
    // Класс для хеширования и проверки паролей
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100000;

        public static string HashPassword(string password)
        {
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            byte[] hash = Pbkdf2(password, salt, Iterations, HashSize);

            return $"{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        public static bool VerifyPassword(string password, string passwordHash)
        {
            try
            {
                string[] parts = passwordHash.Split(':');
                if (parts.Length != 3)
                    return false;

                int iterations = int.Parse(parts[0]);
                byte[] salt = Convert.FromBase64String(parts[1]);
                byte[] expectedHash = Convert.FromBase64String(parts[2]);

                byte[] actualHash = Pbkdf2(password, salt, iterations, expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }
        private static byte[] Pbkdf2(string password, byte[] salt, int iterations, int outputBytes)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(outputBytes);
            }
        }
    }

    // Проверка сложности пароля
    public static class PasswordPolicy
    {
        public static bool IsStrong(string password, out string error)
        {
            error = "";

            if (string.IsNullOrWhiteSpace(password))
            {
                error = "Password is required";
                return false;
            }

            if (password.Length < 8)
            {
                error = "Password must be at least 8 characters";
                return false;
            }

            bool hasUpper = password.Any(char.IsUpper);
            bool hasLower = password.Any(char.IsLower);
            bool hasDigit = password.Any(char.IsDigit);
            bool hasSymbol = password.Any(ch => !char.IsLetterOrDigit(ch));

            if (!hasUpper || !hasLower || !hasDigit || !hasSymbol)
            {
                error = "Password must include upper, lower, digit and symbol";
                return false;
            }

            if (password.Any(char.IsWhiteSpace))
            {
                error = "Password must not contain spaces";
                return false;
            }

            return true;
        }
    }

    public class MessageRecord
    {
        public long Id;
        public string Sender = "";
        public string Receiver = "";
        public string Text = "";
        public string Time = "";
        public bool IsEdited;
        public bool IsDeleted;
    }
    public class RoomRecord
    {
        public long Id;
        public string Name = "";
        public long OwnerId;
        public string OwnerUsername = "";
        public string CreatedAt = "";
    }
    public static class Database
    {
        private const string dbFile = "uchat.db";

        // Открытие соединения
        public static SqliteConnection Open()
        {
            var db = new SqliteConnection($"Data Source={dbFile}");
            db.Open();
            using (var pragma = db.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON;";
                pragma.ExecuteNonQuery();
            }
            return db;
        }

        // Инициализация всех таблиц
        public static void Initialize()
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
            -- Пользователи
            CREATE TABLE IF NOT EXISTS Users
            (
                Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT UNIQUE,
                password_hash TEXT,
                created_on    DATETIME DEFAULT (datetime('now')),
                last_seen_on  DATETIME
            );

            -- Сообщения (public, PM, room)
            CREATE TABLE IF NOT EXISTS Messages
            (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SenderId        INTEGER NOT NULL,
                ReceiverKind    TEXT NOT NULL CHECK (ReceiverKind IN ('all','user','room')),
                ReceiverUserId  INTEGER,
                ReceiverRoomId  INTEGER,
                Text            TEXT,
                Time            DATETIME,
                is_edited       INTEGER NOT NULL DEFAULT 0,
                is_deleted      INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (SenderId) REFERENCES Users(Id) ON DELETE CASCADE,
                FOREIGN KEY (ReceiverUserId) REFERENCES Users(Id) ON DELETE CASCADE,
                FOREIGN KEY (ReceiverRoomId) REFERENCES Rooms(Id) ON DELETE CASCADE,
                CONSTRAINT receiver_target_chk CHECK (
                    (ReceiverKind = 'all'  AND ReceiverUserId IS NULL AND ReceiverRoomId IS NULL) OR
                    (ReceiverKind = 'user' AND ReceiverUserId IS NOT NULL AND ReceiverRoomId IS NULL) OR
                    (ReceiverKind = 'room' AND ReceiverRoomId IS NOT NULL AND ReceiverUserId IS NULL)
                )
            );

            -- Комнаты
            CREATE TABLE IF NOT EXISTS Rooms
            (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT UNIQUE,
                OwnerId   INTEGER NOT NULL,
                CreatedAt TEXT,
                FOREIGN KEY (OwnerId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            -- Участники комнат
            CREATE TABLE IF NOT EXISTS RoomMembers
            (
                RoomId   INTEGER NOT NULL,
                UserId   INTEGER NOT NULL,
                PRIMARY KEY (RoomId, UserId),
                FOREIGN KEY (RoomId) REFERENCES Rooms(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            -- Индексы для ускорения типовых выборок
            CREATE INDEX IF NOT EXISTS idx_users_username ON Users(Username);
            CREATE INDEX IF NOT EXISTS idx_messages_sender ON Messages(SenderId);
            CREATE INDEX IF NOT EXISTS idx_messages_receiverUser ON Messages(ReceiverUserId);
            CREATE INDEX IF NOT EXISTS idx_messages_receiverRoom ON Messages(ReceiverRoomId);
            CREATE INDEX IF NOT EXISTS idx_roommembers_user ON RoomMembers(UserId);
            ";
            cmd.ExecuteNonQuery();
        }

        private static long? GetUserId(SqliteConnection db, string username)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Id
                FROM Users
                WHERE Username = @u;
            ";
            cmd.Parameters.AddWithValue("@u", username);
            object? result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value)
                return null;
            return (long)(result);
        }

        private static string? GetUsernameById(SqliteConnection db, long userId)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Username
                FROM Users
                WHERE Id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", userId);
            object? result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value)
                return null;
            return (string)result;
        }

        private static long? GetRoomIdByName(SqliteConnection db, string name)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Id
                FROM Rooms
                WHERE Name = @name;
            ";
            cmd.Parameters.AddWithValue("@name", name);
            object? result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value)
                return null;
            return (long)result;
        }

        // Сохранение сообщения и возврат ID
        public static long SaveMessage(string sender, string receiver, string text)
        {
            using var db = Open();

            long? senderId = GetUserId(db, sender);
            if (senderId == null)
                return -1;

            string receiverKind;
            long? receiverUserId = null;
            long? receiverRoomId = null;

            if (receiver.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                receiverKind = "all";
            }
            else
            {
                var targetRoomId = GetRoomIdByName(db, receiver);
                var targetUserId = GetUserId(db, receiver);

                if (targetRoomId != null)
                {
                    receiverKind = "room";
                    receiverRoomId = targetRoomId;
                }
                else if (targetUserId != null)
                {
                    receiverKind = "user";
                    receiverUserId = targetUserId;
                }
                else
                {
                    // Неизвестный получатель — не сохраняем
                    return -1;
                }
            }

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Messages(SenderId, ReceiverKind, ReceiverUserId, ReceiverRoomId, Text, Time)
                VALUES(@senderId, @kind, @receiverUser, @receiverRoom, @t, @time);
                SELECT last_insert_rowid();
            ";

            cmd.Parameters.AddWithValue("@senderId", senderId);
            cmd.Parameters.AddWithValue("@kind", receiverKind);
            cmd.Parameters.AddWithValue("@receiverUser", (object?)receiverUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@receiverRoom", (object?)receiverRoomId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@t", text);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return (long)cmd.ExecuteScalar()!;
        }

        public static bool TryRegisterUser(string username, string password)
        {
            using var db = Open();

            // Хешируем пароль перед сохранением
            string passwordHash = PasswordHasher.HashPassword(password);
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO Users(Username, password_hash, created_on, last_seen_on)
            VALUES(@u, @p, @created, @lastSeen);
        ";

            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", passwordHash);
            cmd.Parameters.AddWithValue("@created", now);
            cmd.Parameters.AddWithValue("@lastSeen", now);

            try
            {
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                // 19 = UNIQUE constraint failed (Username уже есть)
                return false;
            }
        }

        public static bool CheckUserCredentials(string username, string password)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
            SELECT password_hash
            FROM Users
            WHERE Username = @u;
        ";

            cmd.Parameters.AddWithValue("@u", username);

            object? result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value)
            {
                // Пользователь не найден
                return false;
            }

            string storedPasswordHash = result.ToString()!;

            // Проверяем пароль с использованием безопасного сравнения хешей
            bool valid = PasswordHasher.VerifyPassword(password, storedPasswordHash);

            if (valid)
            {
                using var update = db.CreateCommand();
                update.CommandText = @"
                    UPDATE Users
                    SET last_seen_on = @now
                    WHERE Username = @u;
                ";
                update.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                update.Parameters.AddWithValue("@u", username);
                update.ExecuteNonQuery();
            }

            return valid;
        }
        public static List<MessageRecord> GetPublicHistory()
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT m.Id,
                       su.Username AS SenderName,
                       'all' AS ReceiverName,
                       m.Text,
                       m.Time,
                       m.is_edited,
                       m.is_deleted
                FROM Messages m
                JOIN Users su ON su.Id = m.SenderId
                WHERE m.ReceiverKind = 'all'
                  AND m.is_deleted = 0
                ORDER BY m.Time;
            ";

            using var r = cmd.ExecuteReader();
            var list = new List<MessageRecord>();

            while (r.Read())
            {
                list.Add(new MessageRecord
                {
                    Id = r.GetInt64(0),
                    Sender = r.GetString(1),
                    Receiver = r.GetString(2),
                    Text = r.GetString(3),
                    Time = r.GetString(4),
                    IsEdited = r.GetInt64(5) != 0,
                    IsDeleted = r.GetInt64(6) != 0
                });
            }

            return list;
        }

        public static List<MessageRecord> GetPrivateHistory(string user1, string user2)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT m.Id,
                       su.Username AS SenderName,
                       ru.Username AS ReceiverName,
                       m.Text,
                       m.Time,
                       m.is_edited,
                       m.is_deleted
                FROM Messages m
                JOIN Users su ON su.Id = m.SenderId
                JOIN Users ru ON ru.Id = m.ReceiverUserId
                WHERE m.ReceiverKind = 'user'
                  AND m.is_deleted = 0
                  AND (
                      (su.Username = @u1 AND ru.Username = @u2) OR
                      (su.Username = @u2 AND ru.Username = @u1)
                  )
                ORDER BY m.Time;
            ";

            cmd.Parameters.AddWithValue("@u1", user1);
            cmd.Parameters.AddWithValue("@u2", user2);

            using var r = cmd.ExecuteReader();
            var list = new List<MessageRecord>();

            while (r.Read())
            {
                list.Add(new MessageRecord
                {
                    Id = r.GetInt64(0),
                    Sender = r.GetString(1),
                    Receiver = r.GetString(2),
                    Text = r.GetString(3),
                    Time = r.GetString(4),
                    IsEdited = r.GetInt64(5) != 0,
                    IsDeleted = r.GetInt64(6) != 0
                });
            }

            return list;
        }

        public static List<MessageRecord> GetPersonalHistory(string username)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT m.Id,
                       su.Username AS SenderName,
                       COALESCE(ru.Username, r.Name, 'all') AS ReceiverName,
                       m.Text,
                       m.Time,
                       m.is_edited,
                       m.is_deleted
                FROM Messages m
                JOIN Users su ON su.Id = m.SenderId
                LEFT JOIN Users ru ON ru.Id = m.ReceiverUserId
                LEFT JOIN Rooms r ON r.Id = m.ReceiverRoomId
                WHERE (
                        m.ReceiverKind = 'all'
                     OR su.Username = @me
                     OR (m.ReceiverKind = 'user' AND ru.Username = @me)
                     OR (m.ReceiverKind = 'room' AND EXISTS (
                         SELECT 1
                         FROM RoomMembers rm
                         JOIN Users ru2 ON ru2.Id = rm.UserId
                         WHERE rm.RoomId = m.ReceiverRoomId AND ru2.Username = @me
                     ))
                    )
                  AND m.is_deleted = 0
                ORDER BY m.Time;
            ";

            cmd.Parameters.AddWithValue("@me", username);

            using var r = cmd.ExecuteReader();
            var list = new List<MessageRecord>();

            while (r.Read())
            {
                list.Add(new MessageRecord
                {
                    Id       = r.GetInt64(0),
                    Sender   = r.GetString(1),
                    Receiver = r.GetString(2),
                    Text     = r.GetString(3),
                    Time     = r.GetString(4),
                    IsEdited = r.GetInt64(5) != 0,
                    IsDeleted = r.GetInt64(6) != 0
                });
            }

            return list;
        }

        public static List<MessageRecord> GetRoomHistory(string roomName)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT m.Id,
                       su.Username AS SenderName,
                       r.Name AS ReceiverName,
                       m.Text,
                       m.Time,
                       m.is_edited,
                       m.is_deleted
                FROM Messages m
                JOIN Users su ON su.Id = m.SenderId
                JOIN Rooms r ON r.Id = m.ReceiverRoomId
                WHERE m.ReceiverKind = 'room' AND r.Name = @room
                  AND m.is_deleted = 0
                ORDER BY m.Time;
            ";
            cmd.Parameters.AddWithValue("@room", roomName);

            using var r = cmd.ExecuteReader();
            var list = new List<MessageRecord>();

            while (r.Read())
            {
                list.Add(new MessageRecord
                {
                    Id = r.GetInt64(0),
                    Sender = r.GetString(1),
                    Receiver = r.GetString(2),
                    Text = r.GetString(3),
                    Time = r.GetString(4),
                    IsEdited = r.GetInt64(5) != 0,
                    IsDeleted = r.GetInt64(6) != 0
                });
            }

            return list;
        }
        public static bool EditMessage(long id, string sender, string newText)
        {
            using var db = Open();

            long? senderId = GetUserId(db, sender);
            if (senderId == null)
                return false;

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                UPDATE Messages
                SET Text = @t,
                    is_edited = 1
                WHERE Id = @id AND SenderId = @s AND is_deleted = 0;
            ";

            cmd.Parameters.AddWithValue("@t", newText);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@s", senderId);

            int rows = cmd.ExecuteNonQuery();
            return rows > 0; // true = успешно, false = нельзя редактировать
        }
        public static bool DeleteMessage(long id, string sender)
        {
            using var db = Open();

            long? senderId = GetUserId(db, sender);
            if (senderId == null)
                return false;

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                UPDATE Messages
                SET is_deleted = 1
                WHERE Id = @id AND SenderId = @s;
            ";

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@s", senderId);

            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }
        // ======== работа с комнатами ========
        public static long CreateRoom(string name, string ownerUsername)
        {
            using var db = Open();

            long? ownerId = GetUserId(db, ownerUsername);
            if (ownerId == null)
                throw new InvalidOperationException($"Owner '{ownerUsername}' not found.");

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Rooms(Name, OwnerId, CreatedAt)
                VALUES(@name, @ownerId, @created);
                SELECT last_insert_rowid();
            ";

            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@ownerId", ownerId);
            cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return (long)cmd.ExecuteScalar()!;
        }
        public static RoomRecord? GetRoomByName(string name)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT r.Id, r.Name, r.OwnerId, u.Username, r.CreatedAt
                FROM Rooms r
                JOIN Users u ON u.Id = r.OwnerId
                WHERE r.Name = @name;
            ";
            cmd.Parameters.AddWithValue("@name", name);

            using var r = cmd.ExecuteReader();

            if (!r.Read())
                return null;

            return new RoomRecord
            {
                Id             = r.GetInt64(0),
                Name           = r.GetString(1),
                OwnerId        = r.GetInt64(2),
                OwnerUsername  = r.GetString(3),
                CreatedAt      = r.GetString(4)
            };
        }
        public static bool RoomExists(string name)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM Rooms
                WHERE Name = @name;
            ";
            cmd.Parameters.AddWithValue("@name", name);

            long count = (long)cmd.ExecuteScalar()!;
            return count > 0;
        }
        public static void AddUserToRoom(long roomId, string username)
        {
            using var db = Open();

            long? userId = GetUserId(db, username);
            if (userId == null)
                throw new InvalidOperationException($"User '{username}' not found.");

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO RoomMembers(RoomId, UserId)
                VALUES(@roomId, @userId);
            ";

            cmd.Parameters.AddWithValue("@roomId", roomId);
            cmd.Parameters.AddWithValue("@userId", userId);

            cmd.ExecuteNonQuery();
        }
        public static bool IsUserInRoom(long roomId, string username)
        {
            using var db = Open();

            long? userId = GetUserId(db, username);
            if (userId == null)
                return false;

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM RoomMembers
                WHERE RoomId = @roomId AND UserId = @userId;
            ";

            cmd.Parameters.AddWithValue("@roomId", roomId);
            cmd.Parameters.AddWithValue("@userId", userId);

            long count = (long)cmd.ExecuteScalar()!;
            return count > 0;
        }
        public static List<string> GetRoomMembers(long roomId)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT u.Username
                FROM RoomMembers rm
                JOIN Users u ON u.Id = rm.UserId
                WHERE rm.RoomId = @roomId;
            ";

            cmd.Parameters.AddWithValue("@roomId", roomId);

            using var r = cmd.ExecuteReader();
            var list = new List<string>();

            while (r.Read())
                list.Add(r.GetString(0));

            return list;
        }
        public static void RemoveUserFromRoom(long roomId, string username)
        {
            using var db = Open();

            long? userId = GetUserId(db, username);
            if (userId == null)
                return;

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM RoomMembers
                WHERE RoomId = @roomId AND UserId = @userId;
            ";

            cmd.Parameters.AddWithValue("@roomId", roomId);
            cmd.Parameters.AddWithValue("@userId", userId);

            cmd.ExecuteNonQuery();
        }
        
        public static void DeleteRoomById(long roomId)
        {
            using var db = Open();
            using var tx = db.BeginTransaction();

            var cmdMembers = db.CreateCommand();
            cmdMembers.Transaction = tx;
            cmdMembers.CommandText = @"
                DELETE FROM RoomMembers
                WHERE RoomId = @id;
            ";
            cmdMembers.Parameters.AddWithValue("@id", roomId);
            cmdMembers.ExecuteNonQuery();

            var cmdRoom = db.CreateCommand();
            cmdRoom.Transaction = tx;
            cmdRoom.CommandText = @"
                DELETE FROM Rooms
                WHERE Id = @id;
            ";
            cmdRoom.Parameters.AddWithValue("@id", roomId);
            cmdRoom.ExecuteNonQuery();

            tx.Commit();
        }

        public static List<RoomRecord> GetAllRooms()
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT r.Id, r.Name, r.OwnerId, u.Username, r.CreatedAt
                FROM Rooms r
                JOIN Users u ON u.Id = r.OwnerId
                ORDER BY Name;
            ";

            using var r = cmd.ExecuteReader();
            var list = new List<RoomRecord>();

            while (r.Read())
            {
                list.Add(new RoomRecord
                {
                    Id            = r.GetInt64(0),
                    Name          = r.GetString(1),
                    OwnerId       = r.GetInt64(2),
                    OwnerUsername = r.GetString(3),
                    CreatedAt     = r.GetString(4)
                });
            }

            return list;
        }

        public static bool RenameRoom(string oldName, string newName)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                UPDATE Rooms
                SET Name = @newName
                WHERE Name = @oldName;
            ";

            cmd.Parameters.AddWithValue("@oldName", oldName);
            cmd.Parameters.AddWithValue("@newName", newName);

            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }

        public static void UpdateRoomMessagesName(string oldName, string newName)
        {
            // Сохраняем метод для совместимости: сообщения теперь ссылаются на room_id,
            // поэтому переименование комнаты не требует обновления сообщений.
        }




    }
}
