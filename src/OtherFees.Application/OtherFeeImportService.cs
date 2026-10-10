using System.Collections.Generic;
using System.IO;
using OtherFees.Domain;

namespace OtherFees.Application
{
    public sealed class OtherFeeImportService
    {
        private readonly IProjectRepository projects;
        private readonly IOtherFeeRepository fees;
        private readonly IOtherFeeWorkbookReader reader;

        public OtherFeeImportService(IProjectRepository projects, IOtherFeeRepository fees, IOtherFeeWorkbookReader reader)
        { this.projects = projects; this.fees = fees; this.reader = reader; }

        public FeeImportResult Import(string projectId, Stream content)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new BusinessException(BusinessErrorKind.Validation, "PROJECT_REQUIRED", "ProjectId不能为空。", "ProjectId");
            projectId = projectId.Trim();
            if (!projects.Exists(projectId))
                throw new BusinessException(BusinessErrorKind.NotFound, "PROJECT_NOT_FOUND", "项目不存在。", "ProjectId");

            // 先完整读取并验证工作簿结构；结构不合法时不会写入任何一行。
            IReadOnlyList<FeeImportRow> rows = reader.Read(content);
            var errors = new List<FeeImportIssue>();
            int success = 0, created = 0, updated = 0, failures = 0;
            foreach (FeeImportRow row in rows)
            {
                if (row.Issues.Count > 0)
                {
                    errors.AddRange(row.Issues);
                    failures++;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(row.ProjectId) && row.ProjectId.Trim() != projectId)
                {
                    errors.Add(Issue(row.RowNumber, "ProjectId", "PROJECT_MISMATCH", "表格中的项目Id与请求的ProjectId不一致。"));
                    failures++;
                    continue;
                }
                try
                {
                    OtherFee fee = OtherFee.Create(projectId, row.Name, row.IncomePrice, row.CostPrice, row.Remark);
                    ImportSaveResult saved = fees.ImportUpsert(fee);
                    success++;
                    if (saved.Created) created++; else updated++;
                }
                catch (BusinessException error)
                {
                    errors.Add(Issue(row.RowNumber, error.Field, error.Code, error.Message));
                    failures++;
                }
            }
            return new FeeImportResult
            {
                SuccessCount = success,
                CreatedCount = created,
                UpdatedCount = updated,
                FailureCount = failures,
                Errors = errors.AsReadOnly()
            };
        }

        private static FeeImportIssue Issue(int row, string field, string code, string reason)
            => new FeeImportIssue { RowNumber = row, Field = field, Code = code, Reason = reason };
    }
}
