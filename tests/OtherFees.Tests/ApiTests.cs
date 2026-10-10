using System;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using NPOI.XSSF.UserModel;
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
        private HttpClient NewClient(bool reset) => new HttpClient(new HttpServer(ApiConfiguration.Create(fixture.Service, fixture.ImportService, fixture.Maintenance, reset)))
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

        [Test]
        public async Task ImportEndpoint_AcceptsMultipartAndReturnsRowReport()
        {
            byte[] workbook;
            using (var book = new XSSFWorkbook())
            using (var stream = new System.IO.MemoryStream())
            {
                var sheet = book.CreateSheet("项目汇总清单");
                string[] headers = { "项目Id", "费用名称", "收入价", "成本价", "描述" };
                var header = sheet.CreateRow(0);
                for (int i = 0; i < headers.Length; i++) header.CreateCell(i).SetCellValue(headers[i]);
                var row = sheet.CreateRow(1);
                row.CreateCell(0).SetCellValue("demo-zero"); row.CreateCell(1).SetCellValue("HTTP导入");
                row.CreateCell(2).SetCellValue(12.5); row.CreateCell(3).SetCellValue(3.5);
                book.Write(stream); workbook = stream.ToArray();
            }
            using (var multipart = new MultipartFormDataContent())
            using (var content = new ByteArrayContent(workbook))
            {
                multipart.Add(content, "file", "import.xlsx");
                using (var response = await client.PostAsync("api/projects/demo-zero/other-fees/import", multipart))
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    var report = JObject.Parse(await response.Content.ReadAsStringAsync());
                    Assert.That((int)report["SuccessCount"], Is.EqualTo(1));
                    Assert.That((int)report["CreatedCount"], Is.EqualTo(1));
                    Assert.That((int)report["FailureCount"], Is.Zero);
                }
            }
            Assert.That(fixture.Service.List("demo-zero").Single().Name, Is.EqualTo("HTTP导入"));
        }

        [Test]
        public async Task ImportOriginalWorkbookWhenConfigured()
        {
            string path = Environment.GetEnvironmentVariable("OTHERFEES_ORIGINAL_IMPORT_XLSX");
            if (string.IsNullOrWhiteSpace(path)) Assert.Ignore("设置OTHERFEES_ORIGINAL_IMPORT_XLSX后验证未修改的原始附件。");
            using (var multipart = new MultipartFormDataContent())
            using (var content = new ByteArrayContent(System.IO.File.ReadAllBytes(path)))
            {
                multipart.Add(content, "file", System.IO.Path.GetFileName(path));
                using (var response = await client.PostAsync("api/projects/7-51231123/other-fees/import", multipart))
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    var report = JObject.Parse(await response.Content.ReadAsStringAsync());
                    Assert.That((int)report["SuccessCount"], Is.EqualTo(2));
                    Assert.That((int)report["CreatedCount"], Is.EqualTo(1));
                    Assert.That((int)report["UpdatedCount"], Is.EqualTo(1));
                    Assert.That((int)report["FailureCount"], Is.Zero);
                }
            }
            var imported = fixture.Service.List("7-51231123").Single();
            Assert.That(imported.Name, Is.EqualTo("测试费用1"));
            Assert.That(imported.IncomePrice, Is.EqualTo(50m));
            Assert.That(imported.CostPrice, Is.EqualTo(70m));
        }

        [TestCase(0, "FILE_REQUIRED")]
        [TestCase(2, "FILE_REQUIRED")]
        [TestCase(1, "IMPORT_FILE_INVALID")]
        public async Task ImportRejectsMissingMultipleOrCorruptedFiles(int count, string code)
        {
            using (var multipart = new MultipartFormDataContent())
            {
                for (int i = 0; i < count; i++) multipart.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "broken.xlsx");
                using (var response = await client.PostAsync("api/projects/demo-zero/other-fees/import", multipart))
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                    Assert.That((string)JObject.Parse(await response.Content.ReadAsStringAsync())["Code"], Is.EqualTo(code));
                }
            }
            Assert.That(fixture.Service.List("demo-zero"), Is.Empty);
        }

        [Test]
        public async Task ImportRejectsWrongContentTypeAndMalformedMultipart()
        {
            using (var response = await client.PostAsync("api/projects/demo-zero/other-fees/import", Json("{}")))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            using (var broken = new StringContent("broken multipart body"))
            {
                broken.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/form-data; boundary=missing");
                using (var response = await client.PostAsync("api/projects/demo-zero/other-fees/import", broken))
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                    Assert.That((string)JObject.Parse(await response.Content.ReadAsStringAsync())["Code"], Is.EqualTo("MULTIPART_INVALID"));
                }
            }
            Assert.That(fixture.Service.List("demo-zero"), Is.Empty);
        }
    }
}
