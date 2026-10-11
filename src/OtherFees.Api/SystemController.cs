using System.Net;
using System.Web.Http;
using OtherFees.Application;

namespace OtherFees.Api
{
    public sealed class SystemController : ApiController
    {
        private readonly IDatabaseMaintenance maintenance;
        private readonly bool enableReset;
        public SystemController(IDatabaseMaintenance maintenance, bool enableReset)
        { this.maintenance = maintenance; this.enableReset = enableReset; }

        [HttpGet, Route("api/health")]
        public IHttpActionResult Health() => Ok(new
        {
            Status = "ok",
            Version = typeof(SystemController).Assembly.GetName().Version.ToString(3)
        });

        [HttpPost, Route("api/reset")]
        public IHttpActionResult Reset()
        {
            if (!enableReset) return Content(HttpStatusCode.NotFound, new ApiError("RESET_DISABLED", "当前环境未启用Reset。", null));
            return Ok(maintenance.Reset());
        }
    }
}
