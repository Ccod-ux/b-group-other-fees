using System.Globalization;
using NUnit.Framework;
using OtherFees.Domain;

namespace OtherFees.Tests
{
    [TestFixture]
    public sealed class DomainTests
    {
        [Test]
        public void OriginalExample_UsesIncomeAsRateDenominator()
        {
            var fee = OtherFee.Create("7-51231123", "测试其他费用", 521m, 520m, null);
            Assert.That(fee.Profit, Is.EqualTo(1m));
            Assert.That(fee.ProfitRatePercent, Is.EqualTo(0.19m));
        }

        [Test]
        public void NullIncome_PreservesNullButCalculatesProfitUsingZero()
        {
            var fee = OtherFee.Create("p", "fee", null, 20m, null);
            Assert.That(fee.Income, Is.Null);
            Assert.That(fee.Profit, Is.EqualTo(-20m));
            Assert.That(fee.ProfitRatePercent, Is.Zero);
        }

        [TestCase("0", "20", "-20", "0")]
        [TestCase("50", "70", "-20", "-40")]
        [TestCase("100", "0", "100", "100")]
        [TestCase("-10", "20", "-30", "300")]
        public void ProfitRules(string income, string cost, string profit, string rate)
        {
            var fee = OtherFee.Create("p", "fee", Decimal(income), Decimal(cost), null);
            Assert.That(fee.Profit, Is.EqualTo(Decimal(profit)));
            Assert.That(fee.ProfitRatePercent, Is.EqualTo(Decimal(rate)));
        }

        [TestCase("1.005", "1.01", 101L)]
        [TestCase("-1.005", "-1.01", -101L)]
        [TestCase("0.004", "0", 0L)]
        public void Money_RoundsHalfAwayFromZero(string input, string expected, long cents)
        {
            var money = Money.FromYuan(Decimal(input), "IncomePrice");
            Assert.That(money.Amount, Is.EqualTo(Decimal(expected)));
            Assert.That(money.Cents, Is.EqualTo(cents));
        }

        [Test]
        public void Cost_IsRequired()
            => Assert.That(Assert.Throws<BusinessException>(() => OtherFee.Create("p", "fee", 1m, null, null)).Code, Is.EqualTo("COST_REQUIRED"));

        [TestCase("-1")]
        [TestCase("-0.001")]
        public void NegativeCost_IsRejectedBeforeRounding(string cost)
            => Assert.That(Assert.Throws<BusinessException>(() => OtherFee.Create("p", "fee", 1m, Decimal(cost), null)).Code, Is.EqualTo("COST_NEGATIVE"));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  \t ")]
        public void BlankName_IsRejected(string name)
            => Assert.That(Assert.Throws<BusinessException>(() => OtherFee.Create("p", name, 1m, 0m, null)).Code, Is.EqualTo("NAME_REQUIRED"));

        [TestCase("岩土工程")]
        [TestCase("建筑工程")]
        [TestCase("技术措施费")]
        [TestCase("安全文明施工及其他措施项目")]
        [TestCase("规费")]
        [TestCase("其它工程")]
        [TestCase("设计费")]
        [TestCase("含税合计")]
        public void ReservedName_IsRejected(string name)
            => Assert.That(Assert.Throws<BusinessException>(() => OtherFee.Create("p", name, 1m, 0m, null)).Code, Is.EqualTo("NAME_RESERVED"));

        [Test]
        public void InvalidUpdate_DoesNotPartiallyChangeEntity()
        {
            var fee = OtherFee.Create("p", "old", 10m, 2m, "note");
            Assert.Throws<BusinessException>(() => fee.ChangeDetails("new", 20m, -1m, "changed"));
            Assert.That(fee.Name, Is.EqualTo("old"));
            Assert.That(fee.Income.Amount, Is.EqualTo(10m));
            Assert.That(fee.Remark, Is.EqualTo("note"));
        }

        [Test]
        public void Money_RejectsUnstorableValue()
            => Assert.That(Assert.Throws<BusinessException>(() => Money.FromYuan(decimal.MaxValue, "IncomePrice")).Code, Is.EqualTo("AMOUNT_OUT_OF_RANGE"));

        private static decimal Decimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
