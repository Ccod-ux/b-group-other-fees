using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;
using OtherFees.Application;
using OtherFees.Domain;

namespace OtherFees.Infrastructure
{
    public sealed class OtherFeeWorkbookReader : IOtherFeeWorkbookReader
    {
        private enum Scope { Unknown, Construction, Outside }
        private sealed class Header
        {
            public int Row;
            public bool FiveColumns;
            public Dictionary<string, int> Columns;
            public int this[string key] => Columns[key];
        }

        public IReadOnlyList<FeeImportRow> Read(Stream content)
        {
            if (content == null || !content.CanRead) throw new ImportFormatException("请上传可读取的Excel文件。");
            IWorkbook workbook;
            try { workbook = WorkbookFactory.Create(content); }
            catch (Exception) { throw new ImportFormatException("文件无法作为有效的Excel工作簿读取。"); }
            using (workbook)
            {
                ISheet sheet = workbook.GetSheet("项目汇总清单");
                if (sheet == null) throw new ImportFormatException("缺少工作表“项目汇总清单”。");
                Header header = FindHeader(sheet);
                IFormulaEvaluator evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
                return ReadRows(sheet, header, evaluator);
            }
        }

        private static Header FindHeader(ISheet sheet)
        {
            for (int index = sheet.FirstRowNum; index <= sheet.LastRowNum; index++)
            {
                IRow row = sheet.GetRow(index);
                if (row == null) continue;
                var columns = new Dictionary<string, int>();
                var duplicates = new HashSet<string>();
                for (int column = 0; column < row.LastCellNum; column++)
                {
                    ICell cell = row.GetCell(column);
                    string key = cell?.CellType == CellType.String ? HeaderKey(cell.StringCellValue) : null;
                    if (key == null) continue;
                    if (columns.ContainsKey(key)) duplicates.Add(key); else columns.Add(key, column);
                }
                bool five = columns.ContainsKey("project");
                string[] required = five
                    ? new[] { "project", "name", "income", "cost", "remark" }
                    : new[] { "sequence", "name", "income", "cost", "profit", "area", "unitIncome", "unitCost", "rate", "remark" };
                if (!required.All(columns.ContainsKey)) continue;
                if (duplicates.Count > 0) throw new ImportFormatException("表头存在重复字段，无法确定列归属。");
                return new Header { Row = index, FiveColumns = five, Columns = columns };
            }
            throw new ImportFormatException("缺少完整的五列表头或十列表头，文件结构未导入。");
        }

        private static string HeaderKey(string text)
        {
            switch (Compact(text))
            {
                case "项目Id": case "项目ID": return "project";
                case "费用名称": case "工项名称": case "项目名称": return "name";
                case "收入价": case "收入价(元)": return "income";
                case "成本价": case "成本价(元)": return "cost";
                case "描述": case "备注": return "remark";
                case "序号": return "sequence";
                case "利润": case "利润(元)": return "profit";
                case "建造面积": case "建筑面积": return "area";
                case "单方收入": case "单方收入(元)": return "unitIncome";
                case "单方成本": case "单方成本(元)": return "unitCost";
                case "利润率": case "利润率(%)": return "rate";
                default: return null;
            }
        }

        private static IReadOnlyList<FeeImportRow> ReadRows(ISheet sheet, Header header, IFormulaEvaluator evaluator)
        {
            var result = new List<FeeImportRow>();
            Scope scope = Scope.Unknown;
            for (int index = header.Row + 1; index <= sheet.LastRowNum; index++)
            {
                IRow source = sheet.GetRow(index);
                if (IsBlank(source) || IsRepeatedHeader(source, header)) continue;
                var row = new FeeImportRow { RowNumber = index + 1 };
                row.Name = ReadText(source.GetCell(header["name"]), evaluator, row, "Name");
                if (header.FiveColumns)
                {
                    row.ProjectId = ReadText(source.GetCell(header["project"]), evaluator, row, "ProjectId");
                    if (string.IsNullOrWhiteSpace(row.ProjectId)) Add(row, "ProjectId", "PROJECT_REQUIRED", "项目Id不能为空。");
                }
                else
                {
                    string sequence = ReadText(source.GetCell(header["sequence"]), evaluator, row, "Sequence");
                    string chapter = GetChapter(sequence, row.Name);
                    if (chapter != null)
                    {
                        scope = chapter == "一建设安装工程费" ? Scope.Construction
                            : IsKnownChapter(chapter) ? Scope.Outside : Scope.Unknown;
                        if (scope == Scope.Unknown)
                        {
                            Add(row, "Name", "SECTION_INVALID", "章节标识或标题损坏，已暂停沿用上一章节归属。");
                            result.Add(row);
                        }
                        continue;
                    }
                    if (IsFixedItem(row.Name) || IsTotal(row.Name)) continue;
                    if (string.IsNullOrWhiteSpace(sequence) && IsExplanation(row.Name)
                        && IsBlank(source.GetCell(header["income"])) && IsBlank(source.GetCell(header["cost"]))) continue;
                    // 已确认的其他章节不包含建设安装工程费明细。
                    if (scope == Scope.Outside) continue;
                    if (!IsSequence(sequence))
                    {
                        Add(row, "Sequence", "SEQUENCE_INVALID", "其他费用明细序号须为正整数。");
                        if (!string.IsNullOrWhiteSpace(sequence)) scope = Scope.Unknown;
                    }
                    if (scope != Scope.Construction)
                        Add(row, "Name", "SECTION_UNCLEAR", "无法确认该行属于“一 建设安装工程费”，未导入。");
                }
                row.IncomePrice = ReadAmount(source.GetCell(header["income"]), evaluator, row, "IncomePrice");
                row.CostPrice = ReadAmount(source.GetCell(header["cost"]), evaluator, row, "CostPrice");
                row.Remark = ReadText(source.GetCell(header["remark"]), evaluator, row, "Remark");
                if (string.IsNullOrWhiteSpace(row.Name)) Add(row, "Name", "NAME_REQUIRED", "费用名称不能为空。");
                result.Add(row);
            }
            return result;
        }

        private static string GetChapter(string sequence, string name)
        {
            string first = Compact(sequence), second = Compact(name);
            // 中文序号可在独立的序号列，或在合并后的章节标题中。
            if (Regex.IsMatch(first, "^[一二三四五六七八九十]"))
                return first.TrimEnd('、', '.', '．') + second;
            if (Regex.IsMatch(second, "^[一二三四五六七八九十][、.．]"))
                return Regex.Replace(second, "^([一二三四五六七八九十]+)[、.．]", "$1");
            if (Regex.IsMatch(second, "^[一二三四五六七八九十]+建设安装工程费")) return second;
            if (second == "建设安装工程费" || second == "设计费" || second == "其他费用" || second == "其它费用")
                return "损坏章节" + second;
            return null;
        }

        private static bool IsKnownChapter(string value) => value == "一建设安装工程费" || value == "二设计费"
            || value == "三不含税合计" || value == "四税金(9%)" || value == "五含税合计";
        private static bool IsSequence(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)
            && number > 0 && decimal.Truncate(number) == number;
        private static bool IsTotal(string value) => Compact(value) == "不含税合计" || Compact(value) == "含税合计"
            || Compact(value) == "税金(9%)" || Compact(value) == "小计" || Compact(value) == "合计";
        private static bool IsFixedItem(string value)
        {
            for (int i = 1; i <= 6; i++) if (value == FixedWorkItem.NameForOrder(i)) return true;
            return false;
        }
        private static bool IsExplanation(string value) => value.StartsWith("说明", StringComparison.Ordinal)
            || value.StartsWith("备注", StringComparison.Ordinal) || value.StartsWith("注：", StringComparison.Ordinal)
            || value.Contains("项目汇总清单");
        private static bool IsRepeatedHeader(IRow row, Header header)
            => header.Columns.All(column => row.GetCell(column.Value)?.CellType == CellType.String
                && HeaderKey(row.GetCell(column.Value).StringCellValue) == column.Key);

        private static decimal? ReadAmount(ICell cell, IFormulaEvaluator evaluator, FeeImportRow row, string field)
        {
            if (IsBlank(cell)) return null;
            try
            {
                CellValue value = Evaluate(cell, evaluator);
                if (value.CellType == CellType.Numeric)
                {
                    if (DateUtil.IsCellDateFormatted(cell)) throw new FormatException();
                    return Convert.ToDecimal(value.NumberValue, CultureInfo.InvariantCulture);
                }
                if (value.CellType == CellType.String)
                {
                    string text = value.StringValue.Trim();
                    if (text.Length == 0) return null;
                    text = text.TrimStart('￥', '¥').Replace('，', ',');
                    if (!Regex.IsMatch(text, @"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$")) throw new FormatException();
                    return decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
                }
                if (value.CellType == CellType.Blank) return null;
                throw new FormatException();
            }
            catch (Exception)
            {
                Add(row, field, "AMOUNT_INVALID", "金额须为有效数值、金额文本或可计算的公式，日期、布尔值、错误或超范围结果均无效。");
                return null;
            }
        }

        private static string ReadText(ICell cell, IFormulaEvaluator evaluator, FeeImportRow row, string field)
        {
            if (IsBlank(cell)) return string.Empty;
            try
            {
                CellValue value = Evaluate(cell, evaluator);
                if (value.CellType == CellType.String) return value.StringValue.Trim();
                if (value.CellType == CellType.Numeric) return value.NumberValue.ToString(CultureInfo.InvariantCulture);
                if (value.CellType == CellType.Blank) return string.Empty;
                throw new FormatException();
            }
            catch (Exception)
            {
                Add(row, field, "CELL_INVALID", "单元格包含错误、布尔值或无法计算的公式。");
                return string.Empty;
            }
        }

        private static CellValue Evaluate(ICell cell, IFormulaEvaluator evaluator)
        {
            if (cell.CellType == CellType.Formula) return evaluator.Evaluate(cell);
            if (cell.CellType == CellType.String) return new CellValue(cell.StringCellValue);
            if (cell.CellType == CellType.Numeric) return new CellValue(cell.NumericCellValue);
            if (cell.CellType == CellType.Boolean) return CellValue.ValueOf(cell.BooleanCellValue);
            if (cell.CellType == CellType.Error) return CellValue.GetError(cell.ErrorCellValue);
            return new CellValue(string.Empty);
        }
        private static bool IsBlank(IRow row) => row == null || row.Cells.All(IsBlank);
        private static bool IsBlank(ICell cell) => cell == null || cell.CellType == CellType.Blank
            || (cell.CellType == CellType.String && string.IsNullOrWhiteSpace(cell.StringCellValue));
        private static string Compact(string value) => Regex.Replace((value ?? string.Empty).Trim().Replace("（", "(").Replace("）", ")"), @"\s+", string.Empty);
        private static void Add(FeeImportRow row, string field, string code, string reason)
            => row.Issues.Add(new FeeImportIssue { RowNumber = row.RowNumber, Field = field, Code = code, Reason = reason });
    }
}
