using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using OtherFees.Application;

namespace OtherFees.Api
{
    [RoutePrefix("api/projects")]
    public sealed class ProjectSummaryController : ApiController
    {
        private readonly ProjectSummaryExportService exporter;
        public ProjectSummaryController(ProjectSummaryExportService exporter) { this.exporter = exporter; }

        [HttpGet, Route("{projectId}/summary/export")]
        public HttpResponseMessage Export(string projectId)
        {
            var content = new ByteArrayContent(exporter.Export(projectId));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            { FileName = "project-summary.xlsx", FileNameStar = "项目汇总清单.xlsx" };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            return response;
        }
    }
}
