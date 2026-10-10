using System;
using System.Collections.Generic;
using System.Linq;

namespace OtherFees.Domain
{
    public enum SummaryLineKind { Chapter, FixedItem, OtherFee, Total, Tax }

    public sealed class SummaryLine
    {
        public string Sequence { get; }
        public string Name { get; }
        public SummaryLineKind Kind { get; }
        public string SourceId { get; }
        public decimal? Income { get; }
        public decimal Cost { get; }
        public decimal Profit => (Income ?? 0m) - Cost;
        // Excel保存比例，不使用API已经舍入过的百分数。
        public decimal ProfitRate => !Income.HasValue || Income == 0m ? 0m : Profit / Income.Value;
        public string Remark { get; }

        internal SummaryLine(string sequence, string name, SummaryLineKind kind, decimal? income, decimal cost,
            string sourceId = null, string remark = null)
        { Sequence = sequence; Name = name; Kind = kind; Income = income; Cost = cost; SourceId = sourceId; Remark = remark; }
    }

    public sealed class ProjectSummarySource
    {
        public Project Project { get; }
        public IReadOnlyList<FixedWorkItem> FixedItems { get; }
        public DesignFee Design { get; }
        public IReadOnlyList<OtherFee> OtherFees { get; }

        public ProjectSummarySource(Project project, IReadOnlyList<FixedWorkItem> fixedItems, DesignFee design,
            IReadOnlyList<OtherFee> otherFees)
        { Project = project; FixedItems = fixedItems; Design = design; OtherFees = otherFees; }
    }

    public sealed class ProjectSummary
    {
        public Project Project { get; }
        public IReadOnlyList<SummaryLine> Lines { get; }

        private ProjectSummary(Project project, IReadOnlyList<SummaryLine> lines) { Project = project; Lines = lines; }

        public static ProjectSummary Calculate(ProjectSummarySource source)
        {
            if (source == null || source.Project == null)
                throw new BusinessException(BusinessErrorKind.NotFound, "PROJECT_NOT_FOUND", "项目不存在。", "ProjectId");
            var project = source.Project;
            if (source.FixedItems == null || source.FixedItems.Count != 6 || source.Design == null || source.OtherFees == null
                || !source.FixedItems.Select(x => x.Order).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 6))
                || source.FixedItems.Any(x => x.ProjectId != project.Id) || source.Design.ProjectId != project.Id
                || source.OtherFees.Any(x => x.ProjectId != project.Id))
                throw new BusinessException(BusinessErrorKind.Validation, "SUMMARY_DATA_INCOMPLETE", "固定工项、设计费或项目关联数据不完整，无法导出。", "ProjectId");

            var details = new List<SummaryLine>();
            foreach (var item in source.FixedItems.OrderBy(x => x.Order))
                details.Add(new SummaryLine(item.Order.ToString(System.Globalization.CultureInfo.InvariantCulture), item.Name,
                    SummaryLineKind.FixedItem, item.Income.Amount, item.Cost.Amount, item.Id));
            int sequence = 7;
            foreach (var fee in source.OtherFees.OrderBy(x => x.SortOrder))
                details.Add(new SummaryLine((sequence++).ToString(System.Globalization.CultureInfo.InvariantCulture), fee.Name,
                    SummaryLineKind.OtherFee, fee.Income?.Amount, fee.Cost.Amount, fee.Id, fee.Remark));

            decimal constructionIncome = details.Sum(x => x.Income ?? 0m), constructionCost = details.Sum(x => x.Cost);
            decimal income = constructionIncome + source.Design.Income.Amount, cost = constructionCost + source.Design.Cost.Amount;
            decimal taxIncome = Round(income * project.TaxRate), taxCost = Round(cost * project.TaxRate);
            var lines = new List<SummaryLine> { new SummaryLine("一", "建设安装工程费", SummaryLineKind.Chapter, constructionIncome, constructionCost) };
            lines.AddRange(details);
            lines.Add(new SummaryLine("二", "设计费", SummaryLineKind.Chapter, source.Design.Income.Amount, source.Design.Cost.Amount));
            lines.Add(new SummaryLine("三", "不含税合计", SummaryLineKind.Total, income, cost));
            lines.Add(new SummaryLine("四", "税金（" + (project.TaxRate * 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%）",
                SummaryLineKind.Tax, taxIncome, taxCost));
            lines.Add(new SummaryLine("五", "含税合计", SummaryLineKind.Total, income + taxIncome, cost + taxCost));
            return new ProjectSummary(project, lines.AsReadOnly());
        }

        public decimal Area => Project.BuildingArea ?? 0m;
        public decimal UnitIncome(SummaryLine line) => Area == 0m ? 0m : Round((line.Income ?? 0m) / Area);
        public decimal UnitCost(SummaryLine line) => Area == 0m ? 0m : Round(line.Cost / Area);
        private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
