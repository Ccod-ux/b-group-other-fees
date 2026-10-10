using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using OtherFees.Application;
using OtherFees.Domain;
using OtherFees.Infrastructure;

namespace OtherFees.Tests
{
    [TestFixture]
    public sealed class PersistenceTests
    {
        private TestDatabase fixture;
        [SetUp] public void SetUp() { fixture = new TestDatabase(); }
        [TearDown] public void TearDown() { fixture.Dispose(); }

        [Test]
        public void Reset_RecreatesTwoProjectsWithSixFixedItemsEachAndDesignFee()
        {
            fixture.Service.Create("7-51231123", "fee", 1m, 0m, null);
            var result = fixture.Maintenance.Reset();
            Assert.That(result.ProjectCount, Is.EqualTo(2));
            Assert.That(result.FixedWorkItemCount, Is.EqualTo(12));
            Assert.That(result.DesignFeeCount, Is.EqualTo(2));
            Assert.That(result.OtherFeeCount, Is.Zero);
            using (var connection = fixture.Database.Open())
            using (var command = new SQLiteCommand("SELECT IncomeCents,CostCents FROM FixedWorkItems WHERE ProjectId='7-51231123' AND SortOrder=1", connection))
            using (var reader = command.ExecuteReader())
            {
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.GetInt64(0), Is.EqualTo(2617491));
                Assert.That(reader.GetInt64(1), Is.EqualTo(1374240));
            }
            Assert.That(fixture.Service.List("7-51231123"), Is.Empty);
        }

        [Test]
        public void Restart_KeepsFeeNullIncomeAndExactCents()
        {
            var created = fixture.Service.Create("7-51231123", "fee", null, 12.345m, "memo");
            var newDatabase = new SqliteDatabase(fixture.DatabasePath);
            new DatabaseMaintenance(newDatabase, Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "database/schema.sql"), fixture.SeedPath).EnsureInitialized();
            var restored = new SqliteOtherFeeRepository(newDatabase).Find(created.Id);
            Assert.That(restored.Income, Is.Null);
            Assert.That(restored.Cost.Amount, Is.EqualTo(12.35m));
            Assert.That(restored.Remark, Is.EqualTo("memo"));
            Assert.That(restored.Profit, Is.EqualTo(-12.35m));
        }

        [Test]
        public void Names_AreUniqueWithinProjectAndTrimmed()
        {
            fixture.Service.Create("7-51231123", " fee ", 1m, 0m, null);
            var error = Assert.Throws<BusinessException>(() => fixture.Service.Create("7-51231123", "fee", 2m, 0m, null));
            Assert.That(error.Code, Is.EqualTo("NAME_DUPLICATE"));
            Assert.DoesNotThrow(() => fixture.Service.Create("demo-zero", "fee", 2m, 0m, null));
        }

        [Test]
        public void UnicodeEquivalentNames_AreDuplicates()
        {
            fixture.Service.Create("demo-zero", "caf\u00e9", 1m, 0m, null);
            Assert.That(Assert.Throws<BusinessException>(() => fixture.Service.Create("demo-zero", "cafe\u0301", 2m, 0m, null)).Code, Is.EqualTo("NAME_DUPLICATE"));
        }

        [Test]
        public void Update_KeepsIdAndOrderAndRecalculatesProfit()
        {
            var first = fixture.Service.Create("demo-zero", "first", 521m, 520m, null);
            fixture.Service.Create("demo-zero", "second", 0m, 0m, null);
            var updated = fixture.Service.Update(first.Id, "renamed", 50m, 70m, "new");
            Assert.That(updated.Id, Is.EqualTo(first.Id));
            Assert.That(updated.Profit, Is.EqualTo(-20m));
            Assert.That(updated.ProfitRatePercent, Is.EqualTo(-40m));
            Assert.That(fixture.Service.List("demo-zero").Select(fee => fee.Id).First(), Is.EqualTo(first.Id));
        }

        [Test]
        public void RenameConflict_KeepsDatabaseUnchanged()
        {
            var first = fixture.Service.Create("demo-zero", "first", 10m, 2m, null);
            fixture.Service.Create("demo-zero", "second", 1m, 0m, null);
            Assert.Throws<BusinessException>(() => fixture.Service.Update(first.Id, "second", 100m, 80m, null));
            Assert.That(fixture.Repository.Find(first.Id).Name, Is.EqualTo("first"));
            Assert.That(fixture.Repository.Find(first.Id).Profit, Is.EqualTo(8m));
        }

        [Test]
        public void DeleteAndMissingRecord_AreReported()
        {
            var fee = fixture.Service.Create("demo-zero", "fee", 1m, 0m, null);
            fixture.Service.Delete(fee.Id);
            Assert.That(fixture.Service.List("demo-zero"), Is.Empty);
            Assert.That(Assert.Throws<BusinessException>(() => fixture.Service.Delete(fee.Id)).Code, Is.EqualTo("FEE_NOT_FOUND"));
            Assert.That(Assert.Throws<BusinessException>(() => fixture.Service.Update(fee.Id, "fee", 1m, 0m, null)).Code, Is.EqualTo("FEE_NOT_FOUND"));
        }

        [Test]
        public void UnknownProject_IsRejectedWithoutWriting()
        {
            Assert.That(Assert.Throws<BusinessException>(() => fixture.Service.Create("missing", "fee", 1m, 0m, null)).Code, Is.EqualTo("PROJECT_NOT_FOUND"));
            Assert.That(Assert.Throws<BusinessException>(() => fixture.Service.List("missing")).Code, Is.EqualTo("PROJECT_NOT_FOUND"));
        }

        [Test]
        public void SqlInjectionText_IsStoredAsName()
        {
            const string name = "fee'; DROP TABLE Projects; --";
            fixture.Service.Create("demo-zero", name, 1m, 0m, null);
            Assert.That(fixture.Service.List("demo-zero").Single().Name, Is.EqualTo(name));
            Assert.That(fixture.Service.List("7-51231123"), Is.Empty);
        }

        [Test]
        public void DatabaseUniqueness_BackstopsApplicationCheck()
        {
            fixture.Repository.Insert(OtherFee.Create("demo-zero", "same", 1m, 0m, null));
            Assert.That(Assert.Throws<BusinessException>(() => fixture.Repository.Insert(OtherFee.Create("demo-zero", "same", 2m, 0m, null))).Code, Is.EqualTo("NAME_DUPLICATE"));
        }

        [Test]
        public async Task ConcurrentCreation_OnlyOneDuplicateNameIsSaved()
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            {
                try { fixture.Service.Create("demo-zero", "same", index, 0m, null); return "created"; }
                catch (BusinessException error) { return error.Code; }
            })));
            Assert.That(results.Count(result => result == "created"), Is.EqualTo(1));
            Assert.That(results.Count(result => result == "NAME_DUPLICATE"), Is.EqualTo(7));
            Assert.That(fixture.Service.List("demo-zero").Count, Is.EqualTo(1));
        }

        [Test]
        public void FailedReset_RollsBackDeletion()
        {
            var created = fixture.Service.Create("demo-zero", "keep", 5m, 2m, null);
            File.WriteAllText(fixture.SeedPath, "{\"Projects\":[]}");
            Assert.Throws<InvalidDataException>(() => fixture.Maintenance.Reset());
            Assert.That(fixture.Repository.Find(created.Id).Profit, Is.EqualTo(3m));
            Assert.That(fixture.Service.List("7-51231123"), Is.Empty);
        }
    }
}
