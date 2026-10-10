namespace OtherFees.Domain
{
    public sealed class DesignFee
    {
        public string ProjectId { get; }
        public Money Income { get; }
        public Money Cost { get; }

        public DesignFee(string projectId, decimal income, decimal cost)
        {
            if (string.IsNullOrWhiteSpace(projectId) || cost < 0)
                throw new BusinessException(BusinessErrorKind.Validation, "DESIGN_FEE_INVALID", "设计费的项目和成本不合法。");
            ProjectId = projectId;
            Income = Money.FromYuan(income, "IncomePrice"); Cost = Money.FromYuan(cost, "CostPrice");
        }
    }
}
