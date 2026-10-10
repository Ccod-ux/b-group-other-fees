using System;

namespace OtherFees.Domain
{
    public enum BusinessErrorKind { Validation, Conflict, NotFound }

    // 业务错误与HTTP无关，由接口层映射成400/409/404。
    public sealed class BusinessException : Exception
    {
        public BusinessErrorKind Kind { get; }
        public string Code { get; }
        public string Field { get; }

        public BusinessException(BusinessErrorKind kind, string code, string message, string field = null)
            : base(message)
        {
            Kind = kind;
            Code = code;
            Field = field;
        }
    }
}
