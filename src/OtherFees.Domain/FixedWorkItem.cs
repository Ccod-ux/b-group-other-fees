using System;

namespace OtherFees.Domain
{
    public sealed class FixedWorkItem
    {
        public static string NameForOrder(int order)
        {
            switch (order)
            {
                case 1: return "岩土工程";
                case 2: return "建筑工程";
                case 3: return "技术措施费";
                case 4: return "安全文明施工及其他措施项目";
                case 5: return "规费";
                case 6: return "其它工程";
                default: throw new ArgumentOutOfRangeException(nameof(order));
            }
        }

        public string Id { get; }
        public string ProjectId { get; }
        public int Order { get; }
        public string Name => NameForOrder(Order);
        public Money Income { get; }
        public Money Cost { get; }

        public FixedWorkItem(string id, string projectId, int order, decimal income, decimal cost)
        {
            NameForOrder(order);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(projectId) || cost < 0)
                throw new BusinessException(BusinessErrorKind.Validation, "FIXED_ITEM_INVALID", "固定工项的标识、项目和成本不合法。");
            Id = id; ProjectId = projectId; Order = order;
            Income = Money.FromYuan(income, "IncomePrice"); Cost = Money.FromYuan(cost, "CostPrice");
        }
    }
}
