using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OtherFees.Api;

namespace OtherFees.Tests
{
    [TestFixture]
    public sealed class ApiTests
    {
        private TestDatabase fixture;
        private HttpClient client;
        [SetUp]
        public void SetUp()
        {
            fixture = new TestDatabase();
            client = NewClient(true);
        }
        [TearDown] public void TearDown() { client.Dispose(); fixture.Dispose(); }
        private HttpClient NewClient(bool reset) => new HttpClient(new HttpServer(ApiConfiguration.Create(fixture.Service, fixture.Maintenance, reset)))
        { BaseAddress = new Uri("http://localhost/"), Timeout = TimeSpan.FromSeconds(20) };
        private static StringContent Json(string value) => new StringContent(value, Encoding.UTF8, "application/json");
        private const string Original = "{\"ProjectId\":\"7-51231123\",\"Name\":\"测试其他费用\",\"IncomePrice\":521,\"CostPrice\":520}";

        [Test]
        public async Task FullCrud_OriginalExampleAndStableId()
        {
            using (var createdResponse = await client.PostAsync("api/other-fees", Json(Original)))
            {
                Assert.That(createdResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
                var created = JObject.Parse(await createdResponse.Content.ReadAsStringAsync());
                Assert.That((decimal)created["Profit"], Is.EqualTo(1m));
                Assert.That((decimal)created["ProfitRatePercent"], Is.EqualTo(0.19m));
                string id = (string)created["Id"];
                using (var list = await client.GetAsync("api/projects/7-51231123/other-fees"))
                    Assert.That(JArray.Parse(await list.Content.ReadAsStringAsync()).Count, Is.EqualTo(1));
                using (var updated = await client.PutAsync("api/other-fees/" + id, Json("{\"Name\":\"测试其他费用\",\"IncomePrice\":50,\"CostPrice\":70}")))
                {
                    Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    var body = JObject.Parse(await updated.Content.ReadAsStringAsync());
                    Assert.That((string)body["Id"], Is.EqualTo(id));
                    Assert.That((decimal)body["ProfitRatePercent"], Is.EqualTo(-40m));
                }
                using (var deleted = await client.DeleteAsync("api/other-fees/" + id))
                    Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                using (var missing = await client.DeleteAsync("api/other-fees/" + id))
                    Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }
        }

        [Test]
        public async Task DuplicateCreate_Returns409()
        {
            using (await client.PostAsync("api/other-fees", Json(Original))) { }
            using (var response = await client.PostAsync("api/other-fees", Json(Original)))
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
                Assert.That((string)JObject.Parse(await response.Content.ReadAsStringAsync())["Code"], Is.EqualTo("NAME_DUPLICATE"));
            }
        }

        [TestCase("{\"ProjectId\":\"demo-zero\",\"Name\":\"fee\",\"CostPrice\":-0.001}", "COST_NEGATIVE")]
        [TestCase("{\"ProjectId\":\"demo-zero\",\"Name\":\"fee\"}", "COST_REQUIRED")]
        [TestCase("{\"ProjectId\":\"demo-zero\",\"Name\":\"fee\",\"CostPrice\":1,\"Profit\":999}", "UNKNOWN_FIELD")]
        [TestCase("{\"ProjectId\":\"demo-zero\",\"Name\":\"fee\",\"CostPrice\":\"oops\"}", "INVALID_JSON")]
        [TestCase("{\"ProjectId\":", "INVALID_JSON")]
        [TestCase("null", "BODY_REQUIRED")]
        public async Task InvalidBody_Returns400AndDoesNotWrite(string json, string code)
        {
            using (var response = await client.PostAsync("api/other-fees", Json(json)))
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That((string)JObject.Parse(await response.Content.ReadAsStringAsync())["Code"], Is.EqualTo(code));
                Assert.That(fixture.Service.List("demo-zero"), Is.Empty);
            }
        }

        [Test]
        public async Task Update_ProjectIdIsNotEditable()
        {
            var created = fixture.Service.Create("demo-zero", "fee", 10m, 2m, null);
            using (var response = await client.PutAsync("api/other-fees/" + created.Id, Json("{\"ProjectId\":\"7-51231123\",\"Name\":\"fee\",\"CostPrice\":0}")))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(fixture.Repository.Find(created.Id).ProjectId, Is.EqualTo("demo-zero"));
        }

        [Test]
        public async Task EmptyAndUnknownProject_AreDifferent()
        {
            using (var response = await client.GetAsync("api/projects/demo-zero/other-fees"))
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(JArray.Parse(await response.Content.ReadAsStringAsync()), Is.Empty);
            }
            using (var response = await client.GetAsync("api/projects/missing/other-fees"))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task ResetEndpoint_ReturnsCountsAndClearsFees()
        {
            fixture.Service.Create("demo-zero", "fee", 1m, 0m, null);
            using (var response = await client.PostAsync("api/reset", null))
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                var body = JObject.Parse(await response.Content.ReadAsStringAsync());
                Assert.That((int)body["FixedWorkItemCount"], Is.EqualTo(12));
                Assert.That((int)body["OtherFeeCount"], Is.Zero);
            }
        }

        [Test]
        public async Task DisabledReset_Returns404AndKeepsData()
        {
            fixture.Service.Create("demo-zero", "keep", 1m, 0m, null);
            using (var restricted = NewClient(false))
            using (var response = await restricted.PostAsync("api/reset", null))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(fixture.Service.List("demo-zero").Count, Is.EqualTo(1));
        }
    }
}
