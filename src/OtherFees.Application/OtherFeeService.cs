using System.Collections.Generic;
using System.Linq;
using OtherFees.Domain;

namespace OtherFees.Application
{
    public sealed class OtherFeeView
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string Name { get; set; }
        public decimal? IncomePrice { get; set; }
        public decimal CostPrice { get; set; }
        public decimal Profit { get; set; }
        public decimal ProfitRatePercent { get; set; }
        public string Remark { get; set; }
    }

    public sealed class OtherFeeService
    {
        private readonly IProjectRepository projects;
        private readonly IOtherFeeRepository fees;
        public OtherFeeService(IProjectRepository projects, IOtherFeeRepository fees)
        { this.projects = projects; this.fees = fees; }

        public OtherFeeView Create(string projectId, string name, decimal? income, decimal? cost, string remark)
        {
            OtherFee fee = OtherFee.Create(projectId, name, income, cost, remark);
            RequireProject(fee.ProjectId);
            RequireUniqueName(fee);
            return View(fees.Insert(fee));
        }

        public IReadOnlyList<OtherFeeView> List(string projectId)
        {
            projectId = projectId?.Trim();
            RequireProject(projectId);
            return fees.FindByProject(projectId).Select(View).ToList();
        }

        public OtherFeeView Update(string id, string name, decimal? income, decimal? cost, string remark)
        {
            OtherFee fee = fees.Find(id);
            if (fee == null) MissingFee();
            fee.ChangeDetails(name, income, cost, remark);
            RequireUniqueName(fee, fee.Id);
            return View(fees.Update(fee));
        }

        public void Delete(string id) { if (!fees.Delete(id)) MissingFee(); }

        private void RequireProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new BusinessException(BusinessErrorKind.Validation, "PROJECT_REQUIRED", "ProjectId不能为空。", "ProjectId");
            if (!projects.Exists(projectId))
                throw new BusinessException(BusinessErrorKind.NotFound, "PROJECT_NOT_FOUND", "项目不存在。", "ProjectId");
        }

        private void RequireUniqueName(OtherFee fee, string exceptId = null)
        {
            fee.EnsureNameAvailable(fees.NameExists(fee.ProjectId, fee.NormalizedName, exceptId));
        }

        public static void MissingFee()
            => throw new BusinessException(BusinessErrorKind.NotFound, "FEE_NOT_FOUND", "其他费用记录不存在。", "Id");

        private static OtherFeeView View(OtherFee fee) => new OtherFeeView
        {
            Id = fee.Id, ProjectId = fee.ProjectId, Name = fee.Name,
            IncomePrice = fee.Income?.Amount, CostPrice = fee.Cost.Amount,
            Profit = fee.Profit, ProfitRatePercent = fee.ProfitRatePercent, Remark = fee.Remark
        };
    }
}
