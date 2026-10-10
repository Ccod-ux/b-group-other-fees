using System;
using System.IO;
using OtherFees.Application;
using OtherFees.Infrastructure;

namespace OtherFees.Tests
{
    internal sealed class TestDatabase : IDisposable
    {
        private readonly string directory;
        public string DatabasePath { get; }
        public string SeedPath { get; }
        public SqliteDatabase Database { get; }
        public DatabaseMaintenance Maintenance { get; }
        public SqliteOtherFeeRepository Repository { get; }
        public OtherFeeService Service { get; }
        public OtherFeeImportService ImportService { get; }
        public ProjectSummaryExportService ExportService { get; }

        public TestDatabase()
        {
            directory = Path.Combine(Path.GetTempPath(), "OtherFees.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            DatabasePath = Path.Combine(directory, "test.sqlite");
            SeedPath = Path.Combine(directory, "seed.json");
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database/seed.json"), SeedPath);
            Database = new SqliteDatabase(DatabasePath);
            Maintenance = new DatabaseMaintenance(Database, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database/schema.sql"), SeedPath);
            Maintenance.EnsureInitialized();
            Repository = new SqliteOtherFeeRepository(Database);
            var projects = new SqliteProjectRepository(Database);
            Service = new OtherFeeService(projects, Repository);
            ImportService = new OtherFeeImportService(projects, Repository, new OtherFeeWorkbookReader());
            ExportService = new ProjectSummaryExportService(new SqliteProjectSummaryRepository(Database), new ProjectSummaryWorkbookWriter());
        }

        public void Dispose()
        {
            // 只清理由本测试创建的独立临时目录；连接均已通过using释放，且未启用连接池。
            string allowedPrefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "OtherFees.Tests-");
            if (!Path.GetFullPath(directory).StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("测试清理路径无效。");
            Directory.Delete(directory, true);
        }
    }
}
