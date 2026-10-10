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

        [Test]
        public void EmptyIncomeIsNull_InvalidDuplicateDoesNotOverwriteOrMoveEarlierRow()
        {
            var original = fixture.Service.Create("demo-zero", "保留顺序", 9m, 1m, "旧备注");
            fixture.Service.Create("demo-zero", "第二条", 2m, 1m, null);
            var result = Import(FiveColumn(
                new object[] { "demo-zero", "保留顺序", null, 7m, "新备注" },
                new object[] { "demo-zero", "保留顺序", 100m, null, "不能覆盖" },
                new object[] { "7-51231123", "项目不匹配", 1m, 0m, null },
                new object[] { "demo-zero", "后续有效", 1.005m, 0m, null }));
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.FailureCount, Is.EqualTo(2));
            Assert.That(result.Errors.Select(error => error.RowNumber), Is.EquivalentTo(new[] { 3, 4 }));
            var saved = fixture.Service.List("demo-zero");
            Assert.That(saved.Select(fee => fee.Name), Is.EqualTo(new[] { "保留顺序", "第二条", "后续有效" }));
            Assert.That(saved[0].Id, Is.EqualTo(original.Id));
            Assert.That(saved[0].IncomePrice, Is.Null);
            Assert.That(saved[0].CostPrice, Is.EqualTo(7m));
            Assert.That(saved[0].Remark, Is.EqualTo("新备注"));
            Assert.That(saved[2].IncomePrice, Is.EqualTo(1.01m));
        }

        [TestCase("项目Id", "费用名称", "收入价", "成本价", "成本价", "描述")]
        [TestCase("项目Id", "费用名称", "收入价", "描述")]
        public void InvalidHeadersNeverWrite(params string[] headers)
        {
            byte[] file = Workbook(sheet =>
            {
                AddRow(sheet, 0, headers.Cast<object>().ToArray());
                AddRow(sheet, 1, new object[] { "demo-zero", "不能保存", 100m, 20m, null });
            });
            Assert.Throws<ImportFormatException>(() => Import(file));
            Assert.That(fixture.Service.List("demo-zero"), Is.Empty);
        }

        [Test]
        public void MissingSheetAndCorruptedFileAreRejected()
        {
            byte[] file = Workbook(sheet => sheet.Workbook.SetSheetName(0, "错误工作表"));
            Assert.Throws<ImportFormatException>(() => Import(file));
            Assert.Throws<ImportFormatException>(() => Import(new byte[] { 1, 2, 3, 4 }));
            Assert.That(fixture.Service.List("demo-zero"), Is.Empty);
        }

        [Test]
        public void InvalidCellTypesHaveFieldErrorsAndFailureCountCountsRows()
        {
            byte[] file = Workbook(sheet =>
            {
                AddRow(sheet, 0, new object[] { "项目Id", "费用名称", "收入价", "成本价", "描述" });
                AddRow(sheet, 1, new object[] { "demo-zero", "多字段失败" });
                sheet.GetRow(1).CreateCell(2).SetCellValue(true);
                sheet.GetRow(1).CreateCell(3).SetCellErrorValue(NPOI.SS.UserModel.FormulaError.VALUE.Code);
                AddRow(sheet, 2, new object[] { "demo-zero", "日期金额", null, 45000m });
                var style = sheet.Workbook.CreateCellStyle();
                style.DataFormat = sheet.Workbook.CreateDataFormat().GetFormat("yyyy-mm-dd");
                sheet.GetRow(2).GetCell(3).CellStyle = style;
                AddRow(sheet, 3, new object[] { "demo-zero", "空公式收入", null, 1m });
                sheet.GetRow(3).CreateCell(2).SetCellFormula("IF(1=1,\"\",0)");
                AddRow(sheet, 4, new object[] { "demo-zero", "空公式成本", 1m });
                sheet.GetRow(4).CreateCell(3).SetCellFormula("IF(1=1,\"\",0)");
                AddRow(sheet, 5, new object[] { "demo-zero", "小负成本", 1m, -0.001m });
                AddRow(sheet, 6, new object[] { "demo-zero", "错误分组", "1,2", 1m });
                AddRow(sheet, 7, new object[] { "demo-zero", "有效末行", "￥1,000.00", 1m });
            });
            var result = Import(file);
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.FailureCount, Is.EqualTo(5));
            Assert.That(result.Errors.Count, Is.EqualTo(6));
            Assert.That(result.Errors.Where(error => error.RowNumber == 2).Select(error => error.Field),
                Is.EquivalentTo(new[] { "IncomePrice", "CostPrice" }));
            Assert.That(fixture.Service.List("demo-zero").Single(fee => fee.Name == "空公式收入").IncomePrice, Is.Null);
            Assert.That(result.Errors.Any(error => error.Code == "COST_NEGATIVE"), Is.True);
        }

        [Test]
        public void Summary_UsesSequenceAndExactTotals_RecoversAfterDamagedChapter()
        {
            byte[] file = Workbook(sheet =>
            {
                AddRow(sheet, 0, SummaryHeaders);
                AddRow(sheet, 1, new object[] { "一", "建设安装工程费", 100m, 20m });
                for (int i = 1; i <= 6; i++) AddRow(sheet, i + 1, new object[] { i, OtherFees.Domain.FixedWorkItem.NameForOrder(i), 10m, 5m });
                AddRow(sheet, 8, new object[] { 7, "税金咨询费", 10m, 5m });
                AddRow(sheet, 9, new object[] { 8, "合计审核费", 9m, 4m });
                AddRow(sheet, 10, new object[] { "一", "建设安装工程费损坏" });
                AddRow(sheet, 11, new object[] { 9, "不能沿用旧章节", 20m, 1m });
                AddRow(sheet, 12, new object[] { "一", "建设安装工程费" });
                AddRow(sheet, 13, new object[] { null, "缺序号", 20m, 1m });
                AddRow(sheet, 14, new object[] { "10", "恢复后的有效费用", 20m, 1m });
                AddRow(sheet, 15, new object[] { "二", "设计费", 50m, 10m });
                AddRow(sheet, 16, new object[] { 1, "设计明细", 40m, 5m });
                AddRow(sheet, 17, new object[] { "三", "不含税合计", 100m, 50m });
                AddRow(sheet, 18, new object[] { "四", "税金（9%）", 9m, 4.5m });
                AddRow(sheet, 19, new object[] { "五", "含税合计", 109m, 54.5m });
            });
            var result = Import(file);
            Assert.That(result.SuccessCount, Is.EqualTo(3));
            Assert.That(result.FailureCount, Is.EqualTo(3));
            Assert.That(result.Errors.Select(error => error.RowNumber).Distinct(), Is.EquivalentTo(new[] { 11, 12, 14 }));
            Assert.That(fixture.Service.List("demo-zero").Select(fee => fee.Name),
                Is.EqualTo(new[] { "税金咨询费", "合计审核费", "恢复后的有效费用" }));
        }

        [Test]
        public void SummaryReorderedHeadersAndDuplicateCostHeader()
        {
            byte[] file = Workbook(sheet =>
            {
                AddRow(sheet, 0, SummaryHeaders.Reverse().ToArray());
                AddRow(sheet, 1, new object[] { "一", "建设安装工程费" }.Concat(new object[8]).Reverse().ToArray());
                AddRow(sheet, 2, new object[] { 7, "换列费用", 20m, 10m }.Concat(new object[6]).Reverse().ToArray());
            });
            Assert.That(Import(file).SuccessCount, Is.EqualTo(1));
            Assert.That(fixture.Service.List("demo-zero").Single().CostPrice, Is.EqualTo(10m));
            byte[] bad = Workbook(sheet => AddRow(sheet, 0, SummaryHeaders.Concat(new object[] { "成本价（元）" }).ToArray()));
            Assert.Throws<ImportFormatException>(() => Import(bad));
            Assert.That(fixture.Service.List("demo-zero").Count, Is.EqualTo(1));
        }

        [Test]
        public void LegacyXlsWorkbookIsSupported()
        {
            using (var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook())
            using (var stream = new MemoryStream())
            {
                var sheet = workbook.CreateSheet("项目汇总清单");
                AddRow(sheet, 0, new object[] { "项目Id", "费用名称", "收入价", "成本价", "描述" });
                AddRow(sheet, 1, new object[] { "demo-zero", "xls费用", 10m, 5m });
                workbook.Write(stream);
                Assert.That(Import(stream.ToArray()).SuccessCount, Is.EqualTo(1));
            }
        }

        private FeeImportResult Import(byte[] file)
        {
            using (var stream = new MemoryStream(file)) return fixture.ImportService.Import("demo-zero", stream);
        }

        private static readonly object[] SummaryHeaders = { "序号", "工项名称", "收入价（元）", "成本价（元）", "利润（元）", "建造面积", "单方收入（元）", "单方成本（元）", "利润率（%）", "备注" };

        private static byte[] FiveColumn(params object[][] values) => Workbook(sheet =>
        {
            string[] headers = { "项目Id", "费用名称", "收入价", "成本价", "描述" };
            AddRow(sheet, 0, headers.Cast<object>().ToArray());
            for (int i = 0; i < values.Length; i++) AddRow(sheet, i + 1, values[i]);
        });

        private static byte[] SummaryWorkbook() => Workbook(sheet =>
        {
            AddRow(sheet, 0, SummaryHeaders);
            AddRow(sheet, 1, new object[] { "一", "建设安装工程费", 100m, 20m });
            AddRow(sheet, 2, new object[] { 1, "岩土工程", 100m, 20m });
            AddRow(sheet, 3, new object[] { null, "说明：以下是其他费用" });
            AddRow(sheet, 4, new object[] { 7, "导入费用", "1,250.50", "450", null, null, null, null, null, "摘要" });
            AddRow(sheet, 5, new object[] { null, "不含税合计", 1250.5m, 450m });
            AddRow(sheet, 6, new object[] { "二", "损坏的章节", 4m, 2m });
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
