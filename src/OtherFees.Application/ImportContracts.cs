using System.Collections.Generic;
using System.IO;

namespace OtherFees.Application
{
    public sealed class FeeImportIssue
    {
        public int RowNumber { get; set; }
        public string Field { get; set; }
        public string Code { get; set; }
        public string Reason { get; set; }
    }

    public sealed class FeeImportRow
    {
        public int RowNumber { get; set; }
        public string ProjectId { get; set; }
        public string Name { get; set; }
        public decimal? IncomePrice { get; set; }
        public decimal? CostPrice { get; set; }
        public string Remark { get; set; }
        public IList<FeeImportIssue> Issues { get; } = new List<FeeImportIssue>();
    }

    public interface IOtherFeeWorkbookReader
    {
        IReadOnlyList<FeeImportRow> Read(Stream content);
    }

    public sealed class FeeImportResult
    {
        public int SuccessCount { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int FailureCount { get; set; }
        public IReadOnlyList<FeeImportIssue> Errors { get; set; }
    }

    public sealed class ImportFormatException : System.Exception
    {
        public ImportFormatException(string message) : base(message) { }
    }
}
