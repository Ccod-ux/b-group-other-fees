using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OtherFees.Application;
using OtherFees.Domain;

namespace OtherFees.Infrastructure
{
    public sealed class DatabaseMaintenance : IDatabaseMaintenance
    {
        private readonly SqliteDatabase database;
        private readonly string schemaPath;
        private readonly string seedPath;
        public DatabaseMaintenance(SqliteDatabase database, string schemaPath, string seedPath)
        { this.database = database; this.schemaPath = schemaPath; this.seedPath = seedPath; }

        public void EnsureInitialized()
        {
            using (var connection = database.Open())
            using (var transaction = connection.BeginTransaction())
            {
                Execute(connection, transaction, File.ReadAllText(schemaPath));
                if (Count(connection, transaction, "Projects") == 0) Seed(connection, transaction);
                transaction.Commit();
            }
        }

        public ResetResult Reset()
        {
            using (var connection = database.Open())
            using (var transaction = connection.BeginTransaction())
            {
                Execute(connection, transaction, File.ReadAllText(schemaPath));
                Execute(connection, transaction, "DELETE FROM OtherFees; DELETE FROM FixedWorkItems; DELETE FROM DesignFees; DELETE FROM Projects;");
                Seed(connection, transaction);
                var result = new ResetResult
                {
                    ProjectCount = Count(connection, transaction, "Projects"),
                    FixedWorkItemCount = Count(connection, transaction, "FixedWorkItems"),
                    DesignFeeCount = Count(connection, transaction, "DesignFees"),
                    OtherFeeCount = Count(connection, transaction, "OtherFees")
                };
                transaction.Commit();
                return result;
            }
        }

        private void Seed(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var seed = JsonConvert.DeserializeObject<SeedFile>(File.ReadAllText(seedPath), new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            if (seed?.Projects == null || seed.Projects.Count == 0) throw new InvalidDataException("初始化项目不能为空。");
            foreach (var input in seed.Projects)
            {
                if (input.FixedWorkItems == null || input.FixedWorkItems.Count != 6
                    || input.FixedWorkItems.Select(item => item.SortOrder).Distinct().Count() != 6 || input.DesignFee == null)
                    throw new InvalidDataException("每个项目必须提供六个固定工项和设计费。");
                var project = new Project(input.ProjectId, input.Name, input.BuildingArea, input.TaxRate);
                using (var command = new SQLiteCommand("INSERT INTO Projects(ProjectId,Name,BuildingArea,TaxRate) VALUES(@id,@name,@area,@tax)", connection, transaction))
                {
                    command.Parameters.AddWithValue("@id", project.Id); command.Parameters.AddWithValue("@name", project.Name);
                    command.Parameters.AddWithValue("@area", project.BuildingArea.HasValue ? (object)project.BuildingArea.Value.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
                    command.Parameters.AddWithValue("@tax", project.TaxRate.ToString(CultureInfo.InvariantCulture)); command.ExecuteNonQuery();
                }
                foreach (var row in input.FixedWorkItems)
                {
                    var item = new FixedWorkItem(project.Id + ":" + row.SortOrder, project.Id, row.SortOrder, row.IncomePrice, row.CostPrice);
                    using (var command = new SQLiteCommand("INSERT INTO FixedWorkItems(Id,ProjectId,SortOrder,Name,IncomeCents,CostCents) VALUES(@id,@project,@order,@name,@income,@cost)", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", item.Id); command.Parameters.AddWithValue("@project", item.ProjectId);
                        command.Parameters.AddWithValue("@order", item.Order); command.Parameters.AddWithValue("@name", item.Name);
                        command.Parameters.AddWithValue("@income", item.Income.Cents); command.Parameters.AddWithValue("@cost", item.Cost.Cents); command.ExecuteNonQuery();
                    }
                }
                var design = new DesignFee(project.Id, input.DesignFee.IncomePrice, input.DesignFee.CostPrice);
                using (var command = new SQLiteCommand("INSERT INTO DesignFees(ProjectId,IncomeCents,CostCents) VALUES(@project,@income,@cost)", connection, transaction))
                {
                    command.Parameters.AddWithValue("@project", design.ProjectId); command.Parameters.AddWithValue("@income", design.Income.Cents);
                    command.Parameters.AddWithValue("@cost", design.Cost.Cents); command.ExecuteNonQuery();
                }
            }
        }

        private static void Execute(SQLiteConnection connection, SQLiteTransaction transaction, string sql)
        { using (var command = new SQLiteCommand(sql, connection, transaction)) command.ExecuteNonQuery(); }

        private static int Count(SQLiteConnection connection, SQLiteTransaction transaction, string table)
        {
            // table只由上面的内部常量传入，不接收用户输入。
            using (var command = new SQLiteCommand("SELECT COUNT(*) FROM " + table, connection, transaction))
                return Convert.ToInt32(command.ExecuteScalar());
        }

        private sealed class SeedFile { public string Source { get; set; } public List<SeedProject> Projects { get; set; } }
        private sealed class SeedProject
        {
            public string ProjectId { get; set; } public string Name { get; set; }
            public decimal? BuildingArea { get; set; } public decimal TaxRate { get; set; }
            public List<SeedFixedItem> FixedWorkItems { get; set; } public SeedPrices DesignFee { get; set; }
        }
        private class SeedPrices { public decimal IncomePrice { get; set; } public decimal CostPrice { get; set; } }
        private sealed class SeedFixedItem : SeedPrices { public int SortOrder { get; set; } }
    }
}
