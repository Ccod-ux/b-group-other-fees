using System;
using System.Data.SQLite;
using System.IO;

namespace OtherFees.Infrastructure
{
    public sealed class SqliteDatabase
    {
        public string FilePath { get; }
        public SqliteDatabase(string filePath)
        {
            FilePath = Path.GetFullPath(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        }

        public SQLiteConnection Open()
        {
            var builder = new SQLiteConnectionStringBuilder { DataSource = FilePath, Version = 3, Pooling = false };
            var connection = new SQLiteConnection(builder.ConnectionString);
            try
            {
                connection.Open();
                using (var command = new SQLiteCommand("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;", connection))
                    command.ExecuteNonQuery();
                return connection;
            }
            catch { connection.Dispose(); throw; }
        }
    }
}
