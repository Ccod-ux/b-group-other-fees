namespace OtherFees.Domain
{
    public sealed class Project
    {
        public string Id { get; }
        public string Name { get; }
        public decimal? BuildingArea { get; }
        public decimal TaxRate { get; }

        public Project(string id, string name, decimal? buildingArea, decimal taxRate)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new BusinessException(BusinessErrorKind.Validation, "PROJECT_INVALID", "项目标识和名称不能为空。", "ProjectId");
            if (buildingArea < 0 || taxRate < 0)
                throw new BusinessException(BusinessErrorKind.Validation, "PROJECT_INVALID", "面积和税率不能为负数。");
            Id = id.Trim(); Name = name.Trim(); BuildingArea = buildingArea; TaxRate = taxRate;
        }
    }
}
