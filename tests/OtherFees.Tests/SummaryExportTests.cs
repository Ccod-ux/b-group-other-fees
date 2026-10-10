using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NUnit.Framework;
using OtherFees.Domain;
using OtherFees.Infrastructure;

namespace OtherFees.Tests
{
    [TestFixture]
    public sealed class SummaryExportTests
    {
        private TestDatabase fixture;
        [SetUp] public void SetUp() { fixture = new TestDatabase(); }
        [TearDown] public void TearDown() { fixture.Dispose(); }
        private XSSFWorkbook Export(string project = "demo-zero") => new XSSFWorkbook(new MemoryStream(fixture.ExportService.Export(project)));
        private static IRow Named(ISheet sheet, string name) => sheet.Cast<IRow>().Single(x => x.GetCell(1)?.StringCellValue == name);
        private static void Amounts(IRow row, double income, double cost, double profit)
        {
            Assert.That(row.GetCell(2).CellType, Is.EqualTo(CellType.Numeric));
            Assert.That(row.GetCell(2).NumericCellValue, Is.EqualTo(income).Within(0.000001));
            Assert.That(row.GetCell(3).NumericCellValue, Is.EqualTo(cost).Within(0.000001));
            Assert.That(row.GetCell(4).NumericCellValue, Is.EqualTo(profit).Within(0.000001));
        }
        private void Execute(string sql)
        {
            using (var connection = fixture.Database.Open())
            using (var command = new SQLiteCommand(sql, connection)) command.ExecuteNonQuery();
        }

        [Test]
        public void PersistedAmounts_RecomputeConstructionTotalsAndSeparateTaxes()
        {
            fixture.Service.Create("7-51231123", "测试费用1", 50m, 70m, "原始附件最终金额");
            using (var book = Export("7-51231123"))
            {
                var sheet = book.GetSheet("项目汇总清单");
                Assert.That(sheet.LastRowNum, Is.EqualTo(13));
                Assert.That(sheet.GetRow(0).GetCell(0).StringCellValue, Is.EqualTo("B组演示项目 - 项目汇总清单"));
                Amounts(Named(sheet, "建设安装工程费"), 34558.40, 122765.73, -88207.33);
                Amounts(Named(sheet, "不含税合计"), 34558.40, 122765.73, -88207.33);
                Amounts(Named(sheet, "税金（9%）"), 3110.26, 11048.92, -7938.66);
                Amounts(Named(sheet, "含税合计"), 37668.66, 133814.65, -96145.99);
                var fee = Named(sheet, "测试费用1");
                Amounts(fee, 50, 70, -20);
                Assert.That(fee.GetCell(0).NumericCellValue, Is.EqualTo(7));
                Assert.That(fee.GetCell(8).NumericCellValue, Is.EqualTo(-0.4));
                Assert.That(fee.GetCell(9).StringCellValue, Is.EqualTo("原始附件最终金额"));
                Assert.That(Named(sheet, "不含税合计").GetCell(8).NumericCellValue,
                    Is.EqualTo(-88207.33 / 34558.40).Within(0.0000001));
            }
        }

        [Test]
        public void SmallProfit_StoresRatioAndCorrectTaxProfit()
        {
            fixture.Service.Create("demo-zero", "校验费用", 521m, 520m, null);
            using (var book = Export())
            {
                var sheet = book.GetSheetAt(0);
                Amounts(Named(sheet, "税金（9%）"), 46.89, 46.80, 0.09);
                Amounts(Named(sheet, "含税合计"), 567.89, 566.80, 1.09);
                var fee = Named(sheet, "校验费用");
                Assert.That(fee.GetCell(8).NumericCellValue, Is.EqualTo(1.0 / 521).Within(0.000000001));
                Assert.That(fee.GetCell(8).CellStyle.GetDataFormatString(), Is.EqualTo("0.00%"));
                Assert.That(new DataFormatter().FormatCellValue(fee.GetCell(8)), Is.EqualTo("0.19%"));
            }
        }

        [TestCase(null, 25, -25)]
        [TestCase(0, 25, -25)]
        [TestCase(-10, 25, -35)]
        public void NullZeroAndNegativeIncome_PreserveValueAndRecompute(double? income, double cost, double profit)
        {
            fixture.Service.Create("demo-zero", "费用", income.HasValue ? (decimal?)income.Value : null, (decimal)cost, null);
            using (var book = Export())
            {
                var sheet = book.GetSheetAt(0); var fee = Named(sheet, "费用");
                Assert.That(fee.GetCell(2).CellType, Is.EqualTo(income.HasValue ? CellType.Numeric : CellType.Blank));
                Assert.That(fee.GetCell(4).NumericCellValue, Is.EqualTo(profit));
                Assert.That(fee.GetCell(8).NumericCellValue, Is.EqualTo(!income.HasValue || income == 0 ? 0 : profit / income.Value));
                Amounts(Named(sheet, "不含税合计"), income ?? 0, cost, profit);
            }
        }

        [Test]
        public void WeightedRate_IsCalculatedFromTotalsInsteadOfAverage()
        {
            fixture.Service.Create("demo-zero", "大额", 100m, 50m, null);
            fixture.Service.Create("demo-zero", "小额", 10m, 20m, null);
            using (var book = Export())
                Assert.That(Named(book.GetSheetAt(0), "不含税合计").GetCell(8).NumericCellValue, Is.EqualTo(40.0 / 110).Within(0.000001));
        }

        [Test]
        public void DesignAreaAndTaxRate_AreReadFromPersistedProject()
        {
            Execute("UPDATE Projects SET Name='数据库项目名称',BuildingArea='10',TaxRate='0.13' WHERE ProjectId='demo-zero'; UPDATE DesignFees SET IncomeCents=10000,CostCents=2000 WHERE ProjectId='demo-zero'");
            fixture.Service.Create("demo-zero", "fee", 50m, 10m, null);
            using (var book = Export())
            {
                var sheet = book.GetSheetAt(0);
                Assert.That(sheet.GetRow(0).GetCell(0).StringCellValue, Is.EqualTo("数据库项目名称 - 项目汇总清单"));
                Amounts(Named(sheet, "设计费"), 100, 20, 80);
                Amounts(Named(sheet, "不含税合计"), 150, 30, 120);
                Amounts(Named(sheet, "税金（13%）"), 19.5, 3.9, 15.6);
                var fee = Named(sheet, "fee");
                Assert.That(fee.GetCell(5).NumericCellValue, Is.EqualTo(10));
                Assert.That(fee.GetCell(6).NumericCellValue, Is.EqualTo(5));
                Assert.That(fee.GetCell(7).NumericCellValue, Is.EqualTo(1));
            }
        }

        [TestCase("NULL")]
        [TestCase("'0'")]
        public void MissingOrZeroArea_OutputZerosAndTaxAuxiliaryCellsAreBlank(string area)
        {
            Execute("UPDATE Projects SET BuildingArea=" + area + " WHERE ProjectId='demo-zero'");
            using (var book = Export())
            {
                var sheet = book.GetSheetAt(0);
                foreach (IRow row in sheet.Cast<IRow>().Skip(2))
                    for (int col = 5; col <= 8; col++)
                        if (row.GetCell(1).StringCellValue == "税金（9%）") Assert.That(row.GetCell(col).CellType, Is.EqualTo(CellType.Blank));
                        else Assert.That(row.GetCell(col).NumericCellValue, Is.Zero);
            }
        }

        [Test]
        public void HalfCentTax_RoundsEachAmountBeforeComputingProfit()
        {
            fixture.Service.Create("demo-zero", "half", 0.50m, 0.06m, null);
            using (var book = Export())
            {
                Amounts(Named(book.GetSheetAt(0), "税金（9%）"), 0.05, 0.01, 0.04);
                Amounts(Named(book.GetSheetAt(0), "含税合计"), 0.55, 0.07, 0.48);
            }
        }

        [Test]
        public void UpdatesDeleteAndRestart_UseCurrentDatabaseAndStableOrder()
        {
            var first = fixture.Service.Create("demo-zero", "first", 1m, 0m, null);
            var deleted = fixture.Service.Create("demo-zero", "delete", 99m, 0m, null);
            fixture.Service.Create("demo-zero", "last", 2m, 0m, null);
            fixture.Service.Update(first.Id, "renamed", 50m, 70m, "new");
            fixture.Service.Delete(deleted.Id);
            var restarted = new OtherFees.Application.ProjectSummaryExportService(new SqliteProjectSummaryRepository(new SqliteDatabase(fixture.DatabasePath)), new ProjectSummaryWorkbookWriter());
            using (var book = new XSSFWorkbook(new MemoryStream(restarted.Export("demo-zero"))))
            {
                var sheet = book.GetSheetAt(0);
                Assert.That(sheet.GetRow(9).GetCell(1).StringCellValue, Is.EqualTo("renamed"));
                Assert.That(sheet.GetRow(10).GetCell(1).StringCellValue, Is.EqualTo("last"));
                Assert.That(sheet.GetRow(10).GetCell(0).NumericCellValue, Is.EqualTo(8));
                Amounts(Named(sheet, "不含税合计"), 52, 70, -18);
                Assert.That(fixture.Repository.Find(first.Id).Id, Is.EqualTo(first.Id));
            }
        }

        [Test]
        public void EmptyProjectAndIsolation_StillExportAllFixedItemsAndChapters()
        {
            fixture.Service.Create("7-51231123", "private", 99m, 0m, null);
            using (var book = Export())
            {
                var sheet = book.GetSheetAt(0);
                Assert.That(sheet.LastRowNum, Is.EqualTo(12));
                Assert.That(sheet.Cast<IRow>().Skip(3).Take(6).Select(x => x.GetCell(0).NumericCellValue), Is.EqualTo(new double[] { 1, 2, 3, 4, 5, 6 }));
                Amounts(Named(sheet, "含税合计"), 0, 0, 0);
            }
        }

        [TestCase("DELETE FROM DesignFees WHERE ProjectId='demo-zero'")]
        [TestCase("DELETE FROM FixedWorkItems WHERE ProjectId='demo-zero' AND SortOrder=2")]
        public void IncompleteBaseData_RejectsInsteadOfSilentlyReplacingWithZero(string sql)
        {
            Execute(sql);
            Assert.That(Assert.Throws<BusinessException>(() => fixture.ExportService.Export("demo-zero")).Code, Is.EqualTo("SUMMARY_DATA_INCOMPLETE"));
        }

        [TestCase(null, "PROJECT_REQUIRED")]
        [TestCase(" ", "PROJECT_REQUIRED")]
        [TestCase("missing", "PROJECT_NOT_FOUND")]
        public void InvalidProject_ReportsClearError(string project, string code)
            => Assert.That(Assert.Throws<BusinessException>(() => fixture.ExportService.Export(project)).Code, Is.EqualTo(code));

        [Test]
        public void StructureColorsFontsBordersAndDynamicPrintRange_MatchTemplateLayout()
        {
            for (int i = 0; i < 30; i++) fixture.Service.Create("demo-zero", "fee" + i, 1m, 0m, null);
            using (var book = Export())
            {
                var sheet = book.GetSheetAt(0);
                Assert.That(book.NumberOfSheets, Is.EqualTo(1));
                Assert.That(sheet.NumMergedRegions, Is.EqualTo(1));
                Assert.That(sheet.GetMergedRegion(0).FormatAsString(), Is.EqualTo("A1:J1"));
                Assert.That(sheet.GetRow(0).HeightInPoints, Is.EqualTo(28));
                Assert.That(sheet.Cast<IRow>().Skip(1).Select(x => x.HeightInPoints).Distinct(), Is.EqualTo(new float[] { 24 }));
                int[] widths = { 8, 28, 18, 18, 18, 10, 18, 18, 15, 12 };
                for (int col = 0; col < 10; col++) Assert.That(sheet.GetColumnWidth(col), Is.EqualTo(widths[col] * 256));
                int[] sampleRows = { 1, 2, 3, 5, 40 };
                string[] colors = { "F5F5F5", "FCE4D6", "E2EFDA", "DDEBF7", "FFF2CC" };
                for (int i = 0; i < sampleRows.Length; i++)
                {
                    var style = (XSSFCellStyle)sheet.GetRow(sampleRows[i]).GetCell(1).CellStyle;
                    Assert.That(BitConverter.ToString(style.FillForegroundXSSFColor.RGB).Replace("-", ""), Is.EqualTo(colors[i]));
                }
                Assert.That(book.GetFontAt(sheet.GetRow(0).GetCell(0).CellStyle.FontIndex).IsBold, Is.True);
                foreach (IRow row in sheet)
                    foreach (ICell cell in row)
                    {
                        Assert.That(cell.CellStyle.BorderBottom, Is.EqualTo(BorderStyle.Thin));
                        Assert.That(cell.CellStyle.BorderTop, Is.EqualTo(BorderStyle.Thin));
                        Assert.That(cell.CellStyle.BorderLeft, Is.EqualTo(BorderStyle.Thin));
                        Assert.That(cell.CellStyle.BorderRight, Is.EqualTo(BorderStyle.Thin));
                        Assert.That(book.GetFontAt(cell.CellStyle.FontIndex).FontName, Is.EqualTo("微软雅黑"));
                        if (row.RowNum >= 2 && cell.ColumnIndex >= 2 && cell.ColumnIndex <= 8)
                            Assert.That(cell.CellStyle.Alignment, Is.EqualTo(HorizontalAlignment.Right));
                    }
                Assert.That(sheet.GetMargin(MarginType.LeftMargin), Is.EqualTo(0.7));
                Assert.That(sheet.GetMargin(MarginType.TopMargin), Is.EqualTo(0.75));
                Assert.That(book.GetPrintArea(0), Does.EndWith("$A$1:$J$43"));
                Assert.That(book.NumCellStyles, Is.LessThan(25));
            }
        }

        [Test]
        public void LiteralText_IsNotWrittenAsExcelFormula()
        {
            fixture.Service.Create("demo-zero", "=1+2", 1m, 0m, "=SUM(C1:C9)");
            using (var book = Export())
            {
                var fee = Named(book.GetSheetAt(0), "=1+2");
                Assert.That(fee.GetCell(1).CellType, Is.EqualTo(CellType.String));
                Assert.That(fee.GetCell(9).CellType, Is.EqualTo(CellType.String));
            }
        }

        [Test]
        public void WorkbookWriter_CanServeConcurrentRequestsWithoutSharedStyles()
        {
            fixture.Service.Create("demo-zero", "并发", 521m, 520m, null);
            Parallel.For(0, 6, i => { using (var book = Export()) Amounts(Named(book.GetSheetAt(0), "含税合计"), 567.89, 566.8, 1.09); });
        }

        [Test]
        public void ExportedSummary_ReimportsOnlyOtherFeesWithStableIds()
        {
            var fee = fixture.Service.Create("demo-zero", "回读", null, 25m, "保存备注");
            using (var stream = new MemoryStream(fixture.ExportService.Export("demo-zero")))
            {
                var report = fixture.ImportService.Import("demo-zero", stream);
                Assert.That(report.SuccessCount, Is.EqualTo(1)); Assert.That(report.UpdatedCount, Is.EqualTo(1));
                Assert.That(report.FailureCount, Is.Zero);
            }
            Assert.That(fixture.Service.List("demo-zero").Single().Id, Is.EqualTo(fee.Id));
            Assert.That(fixture.Repository.Find(fee.Id).Income, Is.Null);
        }
    }
}
