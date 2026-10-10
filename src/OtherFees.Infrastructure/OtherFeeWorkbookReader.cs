using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;
using OtherFees.Application;

namespace OtherFees.Infrastructure
{
    public sealed class OtherFeeWorkbookReader : IOtherFeeWorkbookReader
    {
        private enum Format { FiveColumns, Summary }
        private sealed class Header
        {
            public int Row;
            public Format Format;
            public int ProjectId = -1, Name = -1, Income = -1, Cost = -1, Remark = -1;
        }

        public IReadOnlyList<FeeImportRow> Read(Stream content)
        {
            if (content == null || !content.CanRead) throw new ImportFormatException("请上传可读取的Excel文件。");
            IWorkbook workbook;
            try { workbook = WorkbookFactory.Create(content); }
            catch (Exception) { throw new ImportFormatException("文件无法作为有效的Excel工作簿读取。"); }
            using (workbook)
            {
                if (workbook.NumberOfSheets == 0) throw new ImportFormatException("Excel文件没有工作表。");
                ISheet sheet = FindSheet(workbook);
                Header header = FindHeader(sheet);
                return header.Format == Format.FiveColumns ? ReadFiveColumnRows(sheet, header) : ReadSummaryRows(sheet, header);
            }
        }

        private static ISheet FindSheet(IWorkbook workbook)
        {
            for (int i = 0; i < workbook.NumberOfSheets; i++)
                if (Normalize(workbook.GetSheetName(i)) == "项目汇总清单") return workbook.GetSheetAt(i);
            return workbook.GetSheetAt(0);
        }

        private static Header FindHeader(ISheet sheet)
        {
            int last = Math.Min(sheet.LastRowNum, 29);
            for (int rowIndex = 0; rowIndex <= last; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null) continue;
                var fields = new Dictionary<string, int>(StringComparer.Ordinal);
                var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
                for (int column = 0; column < Math.Max(row.LastCellNum, (short)0); column++)
                {
                    string key = HeaderKey(CellText(row.GetCell(column)));
                    if (key == null) continue;
                    if (fields.ContainsKey(key)) duplicateKeys.Add(key);
                    else fields.Add(key, column);
                }
                bool five = fields.ContainsKey("project") && fields.ContainsKey("name") && fields.ContainsKey("income") && fields.ContainsKey("cost");
                bool summary = fields.ContainsKey("sequence") && fields.ContainsKey("name") && fields.ContainsKey("income")
                    && fields.ContainsKey("cost") && fields.ContainsKey("remark") && fields.ContainsKey("profit") && fields.ContainsKey("rate");
                if (!five && !summary) continue;
                bool duplicateRequired = duplicateKeys.Any(key => key != "cost" || five);
                if (duplicateRequired) throw new ImportFormatException("表头存在重复字段，请检查费用名称、收入价、成本价等关键列。");
                return new Header
                {
                    Row = rowIndex,
                    Format = five ? Format.FiveColumns : Format.Summary,
                    ProjectId = Get(fields, "project"), Name = Get(fields, "name"),
                    Income = Get(fields, "income"), Cost = Get(fields, "cost"), Remark = Get(fields, "remark")
                };
            }
            throw new ImportFormatException("未找到可识别的五列表头或十列表头，文件结构未导入。");
        }

        private static int Get(IDictionary<string, int> fields, string key) => fields.TryGetValue(key, out int value) ? value : -1;

        private static string HeaderKey(string text)
        {
            string value = Regex.Replace(Normalize(text).Replace("（", "(").Replace("）", ")"), @"\s+", string.Empty);
            switch (value)
            {
                case "项目Id": case "项目ID": return "project";
                case "费用名称": case "项目名称": return "name";
                case "收入价": case "收入价(元)": return "income";
                case "成本价": case "成本价(元)": return "cost";
                case "描述": case "备注": return "remark";
                case "序号": return "sequence";
                case "利润": case "利润(元)": return "profit";
                case "利润率": case "利润率(%)": return "rate";
                default: return null;
            }
        }

        private static IReadOnlyList<FeeImportRow> ReadFiveColumnRows(ISheet sheet, Header header)
        {
            var rows = new List<FeeImportRow>();
            for (int index = header.Row + 1; index <= sheet.LastRowNum; index++)
            {
                IRow source = sheet.GetRow(index);
                if (IsBlank(source)) continue;
                var row = new FeeImportRow { RowNumber = index + 1 };
                row.ProjectId = CellText(source.GetCell(header.ProjectId)).Trim();
                row.Name = CellText(source.GetCell(header.Name)).Trim();
                row.Remark = EmptyToNull(CellText(source.GetCell(header.Remark)).Trim());
                row.IncomePrice = ReadAmount(source.GetCell(header.Income), row, "IncomePrice", "收入价", true);
                row.CostPrice = ReadAmount(source.GetCell(header.Cost), row, "CostPrice", "成本价", false);
                if (string.IsNullOrWhiteSpace(row.ProjectId)) Add(row, "ProjectId", "PROJECT_REQUIRED", "项目Id不能为空。");
                if (string.IsNullOrWhiteSpace(row.Name)) Add(row, "Name", "NAME_REQUIRED", "费用名称不能为空。");
                rows.Add(row);
            }
            return rows;
        }

        private static IReadOnlyList<FeeImportRow> ReadSummaryRows(ISheet sheet, Header header)
        {
            var rows = new List<FeeImportRow>();
            bool inOtherFeesSection = false;
            for (int index = header.Row + 1; index <= sheet.LastRowNum; index++)
            {
                IRow source = sheet.GetRow(index);
                if (IsBlank(source)) continue;
                string name = CellText(source.GetCell(header.Name)).Trim();
                bool hasAmounts = !IsBlank(source.GetCell(header.Income)) || !IsBlank(source.GetCell(header.Cost));
                if (IsNumberedSectionLabel(name))
                {
                    inOtherFeesSection = IsOtherFeesSection(name);
                    if (hasAmounts)
                    {
                        var suspicious = new FeeImportRow { RowNumber = index + 1, Name = name };
                        Add(suspicious, "Name", "SECTION_ROW_HAS_DATA", "章节标题行包含金额，无法安全判断这些金额属于哪个费用，未导入。");
                        rows.Add(suspicious);
                    }
                    continue;
                }
                if (!hasAmounts && IsSectionLabel(name))
                {
                    inOtherFeesSection = IsOtherFeesSection(name);
                    continue;
                }
                if (!hasAmounts && string.IsNullOrWhiteSpace(name)) continue;
                if (IsNonBusinessLine(name)) continue;

                var row = new FeeImportRow { RowNumber = index + 1, Name = name };
                if (!inOtherFeesSection)
                    Add(row, "Name", "SECTION_UNCLEAR", "该行不在可识别的“其他费用”章节内，未导入以避免错误归类。");
                row.IncomePrice = ReadAmount(source.GetCell(header.Income), row, "IncomePrice", "收入价", true);
                row.CostPrice = ReadAmount(source.GetCell(header.Cost), row, "CostPrice", "成本价", false);
                row.Remark = EmptyToNull(CellText(source.GetCell(header.Remark)).Trim());
                if (string.IsNullOrWhiteSpace(row.Name)) Add(row, "Name", "NAME_REQUIRED", "费用名称不能为空。");
                rows.Add(row);
            }
            return rows;
        }

        private static bool IsSectionLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Regex.IsMatch(value, @"^(?:[一二三四五六七八九十]+[、.．]|[0-9]+[、.．]).+")
                || value == "其他费用" || value == "设计费" || value == "建设安装工程费";
        }

        private static bool IsNumberedSectionLabel(string value)
            => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"^(?:[一二三四五六七八九十]+[、.．]|[0-9]+[、.．]).+");

        private static bool IsOtherFeesSection(string value)
            => Regex.IsMatch(value, @"^(?:[一二三四五六七八九十]+[、.．])?(?:其他|其它)费用(?:录入|明细|清单)?$");

        private static bool IsNonBusinessLine(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string value = Normalize(name);
            if (value.Contains("合计") || value.Contains("税金") || value == "设计费" || value == "建设安装工程费") return true;
            for (int i = 1; i <= 6; i++) if (value == OtherFees.Domain.FixedWorkItem.NameForOrder(i)) return true;
            return false;
        }

        private static decimal? ReadAmount(ICell cell, FeeImportRow row, string field, string display, bool nullable)
        {
            if (IsBlank(cell)) return nullable ? (decimal?)null : null;
            try
            {
                if (cell.CellType == CellType.Formula)
                {
                    IWorkbook workbook = cell.Sheet.Workbook;
                    IFormulaEvaluator evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
                    CellValue evaluated = evaluator.Evaluate(cell);
                    if (evaluated == null || evaluated.CellType == CellType.Error)
                        throw new FormatException("公式结果无效。");
                    if (evaluated.CellType == CellType.Numeric) return Convert.ToDecimal(evaluated.NumberValue, CultureInfo.InvariantCulture);
                    if (evaluated.CellType == CellType.String)
                        return string.IsNullOrWhiteSpace(evaluated.StringValue) ? (decimal?)null : ParseText(evaluated.StringValue);
                    throw new FormatException("公式结果不是金额。");
                }
                if (cell.CellType == CellType.Numeric)
                {
                    if (DateUtil.IsCellDateFormatted(cell)) throw new FormatException("日期单元格不能作为金额。");
                    return Convert.ToDecimal(cell.NumericCellValue, CultureInfo.InvariantCulture);
                }
                if (cell.CellType == CellType.String) return ParseText(cell.StringCellValue);
                throw new FormatException("单元格类型不是数值或金额文本。");
            }
            catch (Exception error)
            {
                Add(row, field, "AMOUNT_INVALID", display + "格式无效：" + error.Message);
                return null;
            }
        }

        private static decimal ParseText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0m;
            string normalized = value.Trim().Replace("￥", "").Replace("¥", "").Replace(",", "").Replace("，", "");
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)) return amount;
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)) return amount;
            throw new FormatException("文本不能转换为金额。");
        }

        private static string CellText(ICell cell)
        {
            if (cell == null) return string.Empty;
            if (cell.CellType == CellType.Formula)
            {
                try
                {
                    IFormulaEvaluator evaluator = cell.Sheet.Workbook.GetCreationHelper().CreateFormulaEvaluator();
                    CellValue value = evaluator.Evaluate(cell);
                    if (value == null || value.CellType == CellType.Error) return string.Empty;
                    if (value.CellType == CellType.String) return value.StringValue ?? string.Empty;
                    if (value.CellType == CellType.Numeric) return value.NumberValue.ToString(CultureInfo.InvariantCulture);
                    return string.Empty;
                }
                catch { return string.Empty; }
            }
            if (cell.CellType == CellType.String) return cell.StringCellValue ?? string.Empty;
            if (cell.CellType == CellType.Numeric) return cell.NumericCellValue.ToString(CultureInfo.InvariantCulture);
            return string.Empty;
        }

        private static bool IsBlank(IRow row)
        {
            if (row == null) return true;
            for (int i = row.FirstCellNum; i >= 0 && i < row.LastCellNum; i++) if (!IsBlank(row.GetCell(i))) return false;
            return true;
        }

        private static bool IsBlank(ICell cell) => cell == null || cell.CellType == CellType.Blank
            || (cell.CellType == CellType.String && string.IsNullOrWhiteSpace(cell.StringCellValue));
        private static string Normalize(string value) => (value ?? string.Empty).Trim().Normalize(System.Text.NormalizationForm.FormC);
        private static string EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
        private static void Add(FeeImportRow row, string field, string code, string reason)
            => row.Issues.Add(new FeeImportIssue { RowNumber = row.RowNumber, Field = field, Code = code, Reason = reason });
    }
}
