using System;
using System.IO;
using System.Linq;
using NPOI.XSSF.UserModel;
using NUnit.Framework;
using OtherFees.Application;

namespace OtherFees.Tests
{
    [TestFixture]
    public sealed class ExcelImportTests
    {
        private TestDatabase fixture;
        [SetUp] public void SetUp() { fixture = new TestDatabase(); }
        [TearDown] public void TearDown() { fixture.Dispose(); }

        [Test]
        public void FiveColumnImport_DuplicateNamesUpdateInOrderAndKeepId()
        {
            byte[] file = FiveColumn(
                new object[] { "demo-zero", "重复费用", 100m, 233m, "首次" },
                new object[] { "demo-zero", "重复费用", 50m, 70m, "覆盖" });
            FeeImportResult result;
            using (var stream = new MemoryStream(file)) result = fixture.ImportService.Import("demo-zero", stream);
            var fee = fixture.Service.List("demo-zero").Single();
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.CreatedCount, Is.EqualTo(1));
            Assert.That(result.UpdatedCount, Is.EqualTo(1));
            Assert.That(result.FailureCount, Is.Zero);
            Assert.That(fee.IncomePrice, Is.EqualTo(50m));
            Assert.That(fee.CostPrice, Is.EqualTo(70m));
            Assert.That(fee.Remark, Is.EqualTo("覆盖"));

            string id = fee.Id;
            byte[] replacement = FiveColumn(new object[] { "demo-zero", "重复费用", 60m, 80m, "再次导入" });
            using (var stream = new MemoryStream(replacement)) result = fixture.ImportService.Import("demo-zero", stream);
            fee = fixture.Service.List("demo-zero").Single();
            Assert.That(result.CreatedCount, Is.Zero);
            Assert.That(result.UpdatedCount, Is.EqualTo(1));
            Assert.That(fee.Id, Is.EqualTo(id));
        }

        [Test]
        public void InvalidRowsAreReportedAndDoNotBlockLaterRows()
        {
            byte[] file = FiveColumn(
                new object[] { "demo-zero", "有效前行", 10m, 2m, null },
                new object[] { "demo-zero", "无效成本", 10m, -1m, null },
                new object[] { "demo-zero", "有效后行", 20m, 5m, null });
            FeeImportResult result;
            using (var stream = new MemoryStream(file)) result = fixture.ImportService.Import("demo-zero", stream);
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.FailureCount, Is.EqualTo(1));
            Assert.That(result.Errors.Single().RowNumber, Is.EqualTo(3));
            Assert.That(result.Errors.Single().Field, Is.EqualTo("CostPrice"));
            Assert.That(fixture.Service.List("demo-zero").Select(fee => fee.Name), Is.EquivalentTo(new[] { "有效前行", "有效后行" }));
        }

        [Test]
        public void ReorderedHeaders_TextAmountsAndFormulasAreRead()
        {
            byte[] file = Workbook(sheet =>
            {
                AddRow(sheet, 0, new object[] { "成本价", "描述", "项目Id", "费用名称", "收入价" });
                AddRow(sheet, 1, new object[] { "2.50", "文本金额", "demo-zero", "文本行", "1,250.50" });
                var formulaRow = sheet.CreateRow(2);
                formulaRow.CreateCell(0).SetCellValue(3.5);
                formulaRow.CreateCell(2).SetCellValue("demo-zero"); formulaRow.CreateCell(3).SetCellValue("公式行");
                formulaRow.CreateCell(4).SetCellFormula("600+25.5");
                var errorRow = sheet.CreateRow(3);
                errorRow.CreateCell(0).SetCellValue(1);
                errorRow.CreateCell(2).SetCellValue("demo-zero"); errorRow.CreateCell(3).SetCellValue("错误公式");
                errorRow.CreateCell(4).SetCellFormula("1/0");
            });
            FeeImportResult result;
            using (var stream = new MemoryStream(file)) result = fixture.ImportService.Import("demo-zero", stream);
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.FailureCount, Is.EqualTo(1));
            Assert.That(result.Errors.Single().RowNumber, Is.EqualTo(4));
            var imported = fixture.Service.List("demo-zero").ToDictionary(fee => fee.Name);
            Assert.That(imported["文本行"].IncomePrice, Is.EqualTo(1250.5m));
            Assert.That(imported["公式行"].IncomePrice, Is.EqualTo(625.5m));
        }

        [Test]
        public void TenColumnImport_UsesOtherFeesSectionAndReportsUnclearSection()
        {
            byte[] file = SummaryWorkbook();
            FeeImportResult result;
            using (var stream = new MemoryStream(file)) result = fixture.ImportService.Import("demo-zero", stream);
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.FailureCount, Is.EqualTo(2));
            Assert.That(result.Errors.Any(error => error.Code == "SECTION_UNCLEAR"), Is.True);
            var fee = fixture.Service.List("demo-zero").Single();
            Assert.That(fee.Name, Is.EqualTo("导入费用"));
            Assert.That(fee.IncomePrice, Is.EqualTo(1250.5m));
            Assert.That(fee.CostPrice, Is.EqualTo(450m));
        }

        [Test]
        public void MissingHeaderRejectsWholeWorkbookBeforeWriting()
        {
            byte[] file = Workbook(sheet => sheet.CreateRow(0).CreateCell(0).SetCellValue("not a supported header"));
            Assert.Throws<ImportFormatException>(() =>
            {
                using (var stream = new MemoryStream(file)) fixture.ImportService.Import("demo-zero", stream);
            });
            Assert.That(fixture.Service.List("demo-zero"), Is.Empty);
        }

        private static byte[] FiveColumn(params object[][] values) => Workbook(sheet =>
        {
            string[] headers = { "项目Id", "费用名称", "收入价", "成本价", "描述" };
            AddRow(sheet, 0, headers.Cast<object>().ToArray());
            for (int i = 0; i < values.Length; i++) AddRow(sheet, i + 1, values[i]);
        });

        private static byte[] SummaryWorkbook() => Workbook(sheet =>
        {
            object[] headers = { "序号", "项目名称", "收入价（元）", "成本价（元）", "利润（元）", "利润率（%）", "建筑面积", "建筑单价（元）", "成本价（元）", "备注" };
            AddRow(sheet, 0, headers);
            AddRow(sheet, 1, new object[] { null, "一、建设安装工程费" });
            AddRow(sheet, 2, new object[] { 1, "岩土工程", 100m, 20m });
            AddRow(sheet, 3, new object[] { null, "三、其他费用" });
            AddRow(sheet, 4, new object[] { 7, "导入费用", "1,250.50", "450", null, null, null, null, null, "摘要" });
            AddRow(sheet, 5, new object[] { null, "不含税合计", 1250.5m, 450m });
            AddRow(sheet, 6, new object[] { null, "四、其他费用（章节损坏）", 4m, 2m });
            AddRow(sheet, 7, new object[] { 8, "不能错误归属", 15m, 2m });
        });

        private static byte[] Workbook(Action<NPOI.SS.UserModel.ISheet> build)
        {
            using (var workbook = new XSSFWorkbook())
            using (var stream = new MemoryStream())
            {
                build(workbook.CreateSheet("项目汇总清单"));
                workbook.Write(stream);
                return stream.ToArray();
            }
        }

        private static void AddRow(NPOI.SS.UserModel.ISheet sheet, int index, object[] values)
        {
            var row = sheet.CreateRow(index);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null) continue;
                var cell = row.CreateCell(i);
                if (values[i] is decimal amount) cell.SetCellValue(Convert.ToDouble(amount));
                else if (values[i] is int number) cell.SetCellValue(number);
                else cell.SetCellValue(Convert.ToString(values[i]));
            }
        }
    }
}
