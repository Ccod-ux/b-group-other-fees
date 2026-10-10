using System;
using System.Configuration;
using System.IO;
using System.Threading;
using Microsoft.Owin.Hosting;
using OtherFees.Application;
using OtherFees.Infrastructure;
using Owin;

namespace OtherFees.Api
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string url = ConfigurationManager.AppSettings["BaseUrl"] ?? "http://localhost:5080/";
                string dataPath = ConfigurationManager.AppSettings["DatabasePath"] ?? "App_Data/other-fees.sqlite";
                bool enableReset = bool.Parse(ConfigurationManager.AppSettings["EnableReset"] ?? "true");
                for (int index = 0; index < args.Length; index++)
                {
                    if (args[index] == "--url" && index + 1 < args.Length) url = args[++index];
                    else if (args[index] == "--database" && index + 1 < args.Length) dataPath = args[++index];
                    else if (args[index] == "--reset-disabled") enableReset = false;
                    else throw new ArgumentException("支持参数：--url 地址 --database 文件路径 --reset-disabled");
                }
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                if (!Path.IsPathRooted(dataPath)) dataPath = Path.Combine(basePath, dataPath);
                var database = new SqliteDatabase(dataPath);
                var maintenance = new DatabaseMaintenance(database, Path.Combine(basePath, "database/schema.sql"), Path.Combine(basePath, "database/seed.json"));
                maintenance.EnsureInitialized();
                var projects = new SqliteProjectRepository(database);
                var repository = new SqliteOtherFeeRepository(database);
                var service = new OtherFeeService(projects, repository);
                var importer = new OtherFeeImportService(projects, repository, new OtherFeeWorkbookReader());
                var exporter = new ProjectSummaryExportService(new SqliteProjectSummaryRepository(database), new ProjectSummaryWorkbookWriter());
                using (var configuration = ApiConfiguration.Create(service, importer, exporter, maintenance, enableReset))
                using (WebApp.Start(url, app => app.UseWebApi(configuration)))
                using (var stopping = new ManualResetEvent(false))
                {
                    ConsoleCancelEventHandler handler = (sender, e) => { e.Cancel = true; stopping.Set(); };
                    Console.CancelKeyPress += handler;
                    try
                    {
                        Console.WriteLine("OtherFees API: " + url + "api/health");
                        Console.WriteLine("SQLite: " + Path.GetFullPath(dataPath));
                        Console.WriteLine("按 Ctrl+C 停止。重启会保留数据；POST api/reset 会恢复初始数据。");
                        stopping.WaitOne();
                    }
                    finally { Console.CancelKeyPress -= handler; }
                }
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
    }
}
