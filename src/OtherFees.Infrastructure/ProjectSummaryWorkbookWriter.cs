using System.IO;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using OtherFees.Application;
using OtherFees.Domain;

namespace OtherFees.Infrastructure
{
    public sealed class ProjectSummaryWorkbookWriter : IProjectSummaryWorkbookWriter
    {
        public byte[] Write(ProjectSummary summary)
        {
            using (var book = new XSSFWorkbook())
            using (var output = new MemoryStream())
            {
                var sheet = book.CreateSheet("项目汇总清单");
                sheet.DefaultColumnWidth = 9;
                sheet.DefaultRowHeightInPoints = 13.5f;
                int[] widths = { 8, 28, 18, 18, 18, 10, 18, 18, 15, 12 };
                for (int col = 0; col < widths.Length; col++) sheet.SetColumnWidth(col, widths[col] * 256);
                // 原样例没有显式纸张/方向/缩放设置，仅指定了默认页边距。
                sheet.SetMargin(MarginType.LeftMargin, 0.7); sheet.SetMargin(MarginType.RightMargin, 0.7);
                sheet.SetMargin(MarginType.TopMargin, 0.75); sheet.SetMargin(MarginType.BottomMargin, 0.75);
                sheet.SetMargin(MarginType.HeaderMargin, 0.3); sheet.SetMargin(MarginType.FooterMargin, 0.3);
                var titleStyle = Style(book, null, true);
                var headerStyle = Style(book, new byte[] { 245, 245, 245 }, true);
                var chapterStyle = Style(book, new byte[] { 252, 228, 214 }, false);
                var firstFixedStyle = Style(book, new byte[] { 226, 239, 218 }, false);
                var detailStyle = Style(book, new byte[] { 221, 235, 247 }, false);
                var totalStyle = Style(book, new byte[] { 255, 242, 204 }, false);
                var numericStyles = new System.Collections.Generic.Dictionary<int, ICellStyle>();
                var title = Row(sheet, 0, titleStyle, 28f);
                title.GetCell(0).SetCellValue(summary.Project.Name + " - 项目汇总清单");
                sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 9));
                string[] headings = { "序号", "工项名称", "收入价（元）", "成本价（元）", "利润（元）", "建造面积", "单方收入（元）", "单方成本（元）", "利润率（%）", "备注" };
                var header = Row(sheet, 1, headerStyle, 24f);
                for (int col = 0; col < 10; col++) header.GetCell(col).SetCellValue(headings[col]);
                int index = 2;
                foreach (var line in summary.Lines)
                {
                    ICellStyle style = line.Kind == SummaryLineKind.Total ? totalStyle
                        : line.Kind == SummaryLineKind.Chapter || line.Kind == SummaryLineKind.Tax ? chapterStyle
                        : line.Kind == SummaryLineKind.FixedItem && (line.Sequence == "1" || line.Sequence == "2") ? firstFixedStyle : detailStyle;
                    var row = Row(sheet, index++, style, 24f);
                    if (line.Kind == SummaryLineKind.FixedItem || line.Kind == SummaryLineKind.OtherFee)
                        row.GetCell(0).SetCellValue(int.Parse(line.Sequence, System.Globalization.CultureInfo.InvariantCulture));
                    else row.GetCell(0).SetCellValue(line.Sequence);
                    row.GetCell(1).SetCellValue(line.Name);
                    if (line.Income.HasValue) row.GetCell(2).SetCellValue((double)line.Income.Value);
                    row.GetCell(3).SetCellValue((double)line.Cost);
                    row.GetCell(4).SetCellValue((double)line.Profit);
                    if (line.Kind != SummaryLineKind.Tax)
                    {
                        row.GetCell(5).SetCellValue((double)summary.Area);
                        row.GetCell(6).SetCellValue((double)summary.UnitIncome(line));
                        row.GetCell(7).SetCellValue((double)summary.UnitCost(line));
                        row.GetCell(8).SetCellValue((double)line.ProfitRate);
                    }
                    if (line.Remark != null) row.GetCell(9).SetCellValue(line.Remark);
                    // 每类样式只生成一组数字格式，样式数不随明细数量增长。
                    for (int col = 2; col <= 8; col++) row.GetCell(col).CellStyle = NumericStyle(book, style, col == 8, numericStyles);
                }
                book.SetPrintArea(0, 0, 9, 0, index - 1);
                book.Write(output, true);
                return output.ToArray();
            }
        }

        private static ICellStyle NumericStyle(XSSFWorkbook book, ICellStyle source, bool percent,
            System.Collections.Generic.Dictionary<int, ICellStyle> numericStyles)
        {
            int key = source.Index * 2 + (percent ? 1 : 0);
            if (!numericStyles.TryGetValue(key, out var result))
            {
                result = book.CreateCellStyle(); result.CloneStyleFrom(source);
                result.Alignment = HorizontalAlignment.Right;
                result.DataFormat = book.CreateDataFormat().GetFormat(percent ? "0.00%" : "0.00");
                numericStyles.Add(key, result);
            }
            return result;
        }

        private static IRow Row(ISheet sheet, int index, ICellStyle style, float height)
        {
            var row = sheet.CreateRow(index); row.HeightInPoints = height;
            for (int col = 0; col < 10; col++) row.CreateCell(col, CellType.Blank).CellStyle = style;
            return row;
        }

        private static XSSFCellStyle Style(XSSFWorkbook book, byte[] color, bool bold)
        {
            var style = (XSSFCellStyle)book.CreateCellStyle();
            var font = book.CreateFont(); font.FontName = "微软雅黑"; font.Charset = 134; font.FontHeightInPoints = bold ? (short)10 : (short)9; font.IsBold = bold;
            style.SetFont(font); style.Alignment = HorizontalAlignment.Center; style.VerticalAlignment = VerticalAlignment.Center; style.WrapText = true;
            style.BorderTop = style.BorderBottom = style.BorderLeft = style.BorderRight = BorderStyle.Thin;
            if (color != null) { style.SetFillForegroundColor(new XSSFColor(new byte[] { 255, color[0], color[1], color[2] }, null)); style.FillPattern = FillPattern.SolidForeground; }
            return style;
        }
    }
}
