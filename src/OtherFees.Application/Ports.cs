using System.Collections.Generic;
using OtherFees.Domain;

namespace OtherFees.Application
{
    public interface IProjectRepository { bool Exists(string projectId); }

    public interface IOtherFeeRepository
    {
        OtherFee Find(string id);
        IReadOnlyList<OtherFee> FindByProject(string projectId);
        bool NameExists(string projectId, string normalizedName, string exceptId = null);
        OtherFee Insert(OtherFee fee);
        OtherFee Update(OtherFee fee);
        bool Delete(string id);
    }

    public interface IDatabaseMaintenance
    {
        void EnsureInitialized();
        ResetResult Reset();
    }

    public sealed class ResetResult
    {
        public int ProjectCount { get; set; }
        public int FixedWorkItemCount { get; set; }
        public int DesignFeeCount { get; set; }
        public int OtherFeeCount { get; set; }
    }
}
