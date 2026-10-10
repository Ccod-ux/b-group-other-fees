using System;

namespace OtherFees.Domain
{
    // 金额值对象：在业务层用元计算，在SQLite中用整数分保存。
    public sealed class Money
    {
        public decimal Amount { get; }
        public long Cents { get; }

        private Money(decimal amount, long cents) { Amount = amount; Cents = cents; }

        public static Money FromYuan(decimal value, string field)
        {
            decimal rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
            if (rounded > long.MaxValue / 100m || rounded < long.MinValue / 100m)
                throw new BusinessException(BusinessErrorKind.Validation, "AMOUNT_OUT_OF_RANGE", "金额超出可保存范围。", field);
            return new Money(rounded, decimal.ToInt64(rounded * 100m));
        }
    }
}
