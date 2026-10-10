using OtherFees.Domain;

namespace OtherFees.Application
{
    public interface IProjectSummaryRepository { ProjectSummarySource Read(string projectId); }
    public interface IProjectSummaryWorkbookWriter { byte[] Write(ProjectSummary summary); }

    public sealed class ProjectSummaryExportService
    {
        private readonly IProjectSummaryRepository repository;
        private readonly IProjectSummaryWorkbookWriter writer;
        public ProjectSummaryExportService(IProjectSummaryRepository repository, IProjectSummaryWorkbookWriter writer)
        { this.repository = repository; this.writer = writer; }

        public byte[] Export(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                throw new BusinessException(BusinessErrorKind.Validation, "PROJECT_REQUIRED", "ProjectId不能为空。", "ProjectId");
            return writer.Write(ProjectSummary.Calculate(repository.Read(projectId.Trim())));
        }
    }
}
