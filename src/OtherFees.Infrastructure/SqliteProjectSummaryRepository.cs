using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using OtherFees.Application;
using OtherFees.Domain;

namespace OtherFees.Infrastructure
{
    public sealed class SqliteProjectSummaryRepository : IProjectSummaryRepository
    {
        private readonly SqliteDatabase database;
        public SqliteProjectSummaryRepository(SqliteDatabase database) { this.database = database; }

        public ProjectSummarySource Read(string projectId)
        {
            // 四张表在同一读事务中取得一致快照，Excel生成在释放连接后进行。
            using (var connection = database.Open())
            using (var transaction = connection.BeginTransaction())
            {
                Project project;
                using (var command = Command("SELECT Name,BuildingArea,TaxRate FROM Projects WHERE ProjectId=@project", projectId, connection, transaction))
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    project = new Project(projectId, reader.GetString(0), reader.IsDBNull(1) ? (decimal?)null : decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                        decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture));
                }
                var fixedItems = new List<FixedWorkItem>();
                using (var command = Command("SELECT Id,SortOrder,IncomeCents,CostCents FROM FixedWorkItems WHERE ProjectId=@project ORDER BY SortOrder", projectId, connection, transaction))
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) fixedItems.Add(new FixedWorkItem(reader.GetString(0), projectId, reader.GetInt32(1), reader.GetInt64(2) / 100m, reader.GetInt64(3) / 100m));
                DesignFee design = null;
                using (var command = Command("SELECT IncomeCents,CostCents FROM DesignFees WHERE ProjectId=@project", projectId, connection, transaction))
                using (var reader = command.ExecuteReader())
                    if (reader.Read()) design = new DesignFee(projectId, reader.GetInt64(0) / 100m, reader.GetInt64(1) / 100m);
                var fees = new List<OtherFee>();
                using (var command = Command("SELECT rowid,Id,Name,IncomeCents,CostCents,Remark FROM OtherFees WHERE ProjectId=@project ORDER BY rowid", projectId, connection, transaction))
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) fees.Add(OtherFee.Restore(reader.GetString(1), projectId, reader.GetInt64(0), reader.GetString(2),
                        reader.IsDBNull(3) ? (decimal?)null : reader.GetInt64(3) / 100m, reader.GetInt64(4) / 100m, reader.IsDBNull(5) ? null : reader.GetString(5)));
                transaction.Commit();
                return new ProjectSummarySource(project, fixedItems, design, fees);
            }
        }

        private static SQLiteCommand Command(string sql, string projectId, SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var command = new SQLiteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@project", projectId);
            return command;
        }
    }
}
