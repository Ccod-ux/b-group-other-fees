using System.Data.SQLite;
using OtherFees.Application;

namespace OtherFees.Infrastructure
{
    public sealed class SqliteProjectRepository : IProjectRepository
    {
        private readonly SqliteDatabase database;
        public SqliteProjectRepository(SqliteDatabase database) { this.database = database; }

        public bool Exists(string projectId)
        {
            using (var connection = database.Open())
            using (var command = new SQLiteCommand("SELECT COUNT(*) FROM Projects WHERE ProjectId=@id", connection))
            {
                command.Parameters.AddWithValue("@id", projectId);
                return (long)command.ExecuteScalar() > 0;
            }
        }
    }
}
