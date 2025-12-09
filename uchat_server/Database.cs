using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace uchat_server
{
    public class MessageRecord
    {
        public long Id;
        public string Sender = "";
        public string Receiver = "";
        public string Text = "";
        public string Time = "";
    }
    public class RoomRecord
    {
        public long Id;
        public string Name = "";
        public string Owner = "";
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
                Password TEXT
            );

            -- Сообщения (public, PM, room)
            CREATE TABLE IF NOT EXISTS Messages
            (
                Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                Sender   TEXT,
                Receiver TEXT,   -- 'all', имя пользователя или имя комнаты
                Text     TEXT,
                Time     TEXT
            );

            -- Комнаты
            CREATE TABLE IF NOT EXISTS Rooms
            (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT UNIQUE,  -- имя комнаты (room1, chat123 и т.п.)
                Owner     TEXT,         -- имя создателя комнаты
                CreatedAt TEXT
            );

            -- Участники комнат
            CREATE TABLE IF NOT EXISTS RoomMembers
            (
                RoomId   INTEGER NOT NULL,
                Username TEXT NOT NULL,
                PRIMARY KEY (RoomId, Username),
                FOREIGN KEY (RoomId) REFERENCES Rooms(Id)
            );
            ";
            cmd.ExecuteNonQuery();
        }

        // Сохранение сообщения и возврат ID
        public static long SaveMessage(string sender, string receiver, string text)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText =
                "INSERT INTO Messages(Sender,Receiver,Text,Time) " +
                "VALUES(@s,@r,@t,@time); " +
                "SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@s", sender);
            cmd.Parameters.AddWithValue("@r", receiver);
            cmd.Parameters.AddWithValue("@t", text);
            cmd.Parameters.AddWithValue("@time",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return (long)cmd.ExecuteScalar()!;
        }

        // ======== работа с пользователями ========
        /// Пытается зарегистрировать пользователя.
        /// true  = успешно
        /// false = пользователь уже существует (или ошибка уникальности)
        public static bool TryRegisterUser(string username, string password)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO Users(Username, Password)
            VALUES(@u, @p);
        ";

            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", password);

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

        /// Проверяет логин/пароль.
        public static bool CheckUserCredentials(string username, string password)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
            SELECT COUNT(*)
            FROM Users
            WHERE Username = @u AND Password = @p;
        ";

            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", password);

            long count = (long)cmd.ExecuteScalar()!;
            return count > 0;
        }
        public static List<MessageRecord> GetPublicHistory()
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Sender, Receiver, Text, Time
                FROM Messages
                WHERE Receiver = 'all'
                ORDER BY Time;
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
                    Time = r.GetString(4)
                });
            }

            return list;
        }

        public static List<MessageRecord> GetPrivateHistory(string user1, string user2)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Sender, Receiver, Text, Time
                FROM Messages
                WHERE (Sender=@u1 AND Receiver=@u2)
                OR (Sender=@u2 AND Receiver=@u1)
                ORDER BY Time;
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
                    Time = r.GetString(4)
                });
            }

            return list;
        }

        public static List<MessageRecord> GetPersonalHistory(string username)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Sender, Receiver, Text, Time
                FROM Messages
                WHERE Receiver = 'all'
                OR Receiver = 'ALL'
                OR Sender   = @me
                OR Receiver = @me
                ORDER BY Time;
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
                    Time     = r.GetString(4)
                });
            }

            return list;
        }

        public static List<MessageRecord> GetRoomHistory(string roomName)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Sender, Receiver, Text, Time
                FROM Messages
                WHERE Receiver = @room
                ORDER BY Time;
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
                    Time = r.GetString(4)
                });
            }

            return list;
        }
        public static bool EditMessage(long id, string sender, string newText)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                UPDATE Messages
                SET Text = @t
                WHERE Id = @id AND Sender = @s;
            ";

            cmd.Parameters.AddWithValue("@t", newText);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@s", sender);

            int rows = cmd.ExecuteNonQuery();
            return rows > 0; // true = успешно, false = нельзя редактировать
        }
        public static bool DeleteMessage(long id, string sender)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM Messages
                WHERE Id = @id AND Sender = @s;
            ";

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@s", sender);

            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }
        // ======== работа с комнатами ========
        public static long CreateRoom(string name, string owner)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Rooms(Name, Owner, CreatedAt)
                VALUES(@name, @owner, @created);
                SELECT last_insert_rowid();
            ";

            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@owner", owner);
            cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return (long)cmd.ExecuteScalar()!;
        }
        public static RoomRecord? GetRoomByName(string name)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Name, Owner, CreatedAt
                FROM Rooms
                WHERE Name = @name;
            ";
            cmd.Parameters.AddWithValue("@name", name);

            using var r = cmd.ExecuteReader();

            if (!r.Read())
                return null;

            return new RoomRecord
            {
                Id        = r.GetInt64(0),
                Name      = r.GetString(1),
                Owner     = r.GetString(2),
                CreatedAt = r.GetString(3)
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

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO RoomMembers(RoomId, Username)
                VALUES(@roomId, @user);
            ";

            cmd.Parameters.AddWithValue("@roomId", roomId);
            cmd.Parameters.AddWithValue("@user", username);

            cmd.ExecuteNonQuery();
        }
        public static bool IsUserInRoom(long roomId, string username)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM RoomMembers
                WHERE RoomId = @roomId AND Username = @user;
            ";

            cmd.Parameters.AddWithValue("@roomId", roomId);
            cmd.Parameters.AddWithValue("@user", username);

            long count = (long)cmd.ExecuteScalar()!;
            return count > 0;
        }
        public static List<string> GetRoomMembers(long roomId)
        {
            using var db = Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Username
                FROM RoomMembers
                WHERE RoomId = @roomId;
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

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM RoomMembers
                WHERE RoomId = @roomId AND Username = @user;
            ";

            cmd.Parameters.AddWithValue("@roomId", roomId);
            cmd.Parameters.AddWithValue("@user", username);

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










    }
}
