using System;
using System.Text;

namespace OtherFees.Domain
{
    public sealed class OtherFee
    {
        public string Id { get; }
        public string ProjectId { get; }
        public string Name { get; private set; }
        public string NormalizedName => Name;
        public Money Income { get; private set; }
        public Money Cost { get; private set; }
        public string Remark { get; private set; }
        public long SortOrder { get; }

        // 空收入保存为null，仅计算利润时暂按0处理，详见业务约定。
        public decimal Profit => (Income?.Amount ?? 0m) - Cost.Amount;
        public decimal ProfitRatePercent => Income == null || Income.Amount == 0m
            ? 0m : decimal.Round(Profit / Income.Amount * 100m, 2, MidpointRounding.AwayFromZero);

        private OtherFee(string id, string projectId, long sortOrder, string name, decimal? income, decimal? cost, string remark)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new BusinessException(BusinessErrorKind.Validation, "PROJECT_REQUIRED", "ProjectId不能为空。", "ProjectId");
            Id = id; ProjectId = projectId.Trim(); SortOrder = sortOrder;
            ChangeDetails(name, income, cost, remark);
        }

        public static OtherFee Create(string projectId, string name, decimal? income, decimal? cost, string remark)
            => new OtherFee(Guid.NewGuid().ToString("N"), projectId, 0, name, income, cost, remark);

        public static OtherFee Restore(string id, string projectId, long sortOrder, string name, decimal? income, decimal cost, string remark)
            => new OtherFee(id, projectId, sortOrder, name, income, cost, remark);

        // 是否已有同名记录由仓储查询；业务上如何处理重复由领域决定。
        public void EnsureNameAvailable(bool nameExists)
        { if (nameExists) throw DuplicateName(); }

        public static BusinessException DuplicateName()
            => new BusinessException(BusinessErrorKind.Conflict, "NAME_DUPLICATE", "同一项目内费用名称不能重复。", "Name");

        public void ChangeDetails(string name, decimal? income, decimal? cost, string remark)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new BusinessException(BusinessErrorKind.Validation, "NAME_REQUIRED", "费用名称不能为空。", "Name");
            string normalized = name.Trim().Normalize(NormalizationForm.FormC);
            for (int order = 1; order <= 6; order++)
                if (normalized == FixedWorkItem.NameForOrder(order)) ReservedName();
            if (normalized == "建设安装工程费" || normalized == "设计费" || normalized == "不含税合计"
                || normalized == "税金（9%）" || normalized == "含税合计") ReservedName();
            if (!cost.HasValue)
                throw new BusinessException(BusinessErrorKind.Validation, "COST_REQUIRED", "成本价必填。", "CostPrice");
            if (cost.Value < 0m)
                throw new BusinessException(BusinessErrorKind.Validation, "COST_NEGATIVE", "成本价不能为负数。", "CostPrice");

            // 全部验证成功后再改变实体，避免只更新了一半。
            Money newIncome = income.HasValue ? Money.FromYuan(income.Value, "IncomePrice") : null;
            Money newCost = Money.FromYuan(cost.Value, "CostPrice");
            Name = normalized; Income = newIncome; Cost = newCost;
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
        }

        private static void ReservedName()
            => throw new BusinessException(BusinessErrorKind.Validation, "NAME_RESERVED", "其他费用不能使用固定工项或章节名称。", "Name");
    }
}
