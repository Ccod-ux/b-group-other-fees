using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OtherFees.Application;

namespace OtherFees.Api
{
    public class UpdateFeeRequest
    {
        public string Name { get; set; }
        public decimal? IncomePrice { get; set; }
        public decimal? CostPrice { get; set; }
        public string Remark { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> ExtraFields { get; set; }
    }

    public sealed class CreateFeeRequest : UpdateFeeRequest
    {
        public string ProjectId { get; set; }
    }

    public sealed class OtherFeesController : ApiController
    {
        private readonly OtherFeeService fees;
        public OtherFeesController(OtherFeeService fees) { this.fees = fees; }

        [HttpPost, Route("api/other-fees")]
        public IHttpActionResult Create([FromBody] CreateFeeRequest body)
        {
            var invalid = InvalidBody(body);
            if (invalid != null) return invalid;
            var created = fees.Create(body.ProjectId, body.Name, body.IncomePrice, body.CostPrice, body.Remark);
            return Content(HttpStatusCode.Created, created);
        }

        [HttpGet, Route("api/projects/{projectId}/other-fees")]
        public IHttpActionResult List(string projectId) => Ok(fees.List(projectId));

        // PUT替换可编辑字段：收入/备注省略时变为null，成本/名称仍然必填。
        [HttpPut, Route("api/other-fees/{id}")]
        public IHttpActionResult Update(string id, [FromBody] UpdateFeeRequest body)
        {
            var invalid = InvalidBody(body);
            if (invalid != null) return invalid;
            return Ok(fees.Update(id, body.Name, body.IncomePrice, body.CostPrice, body.Remark));
        }

        [HttpDelete, Route("api/other-fees/{id}")]
        public IHttpActionResult Delete(string id)
        { fees.Delete(id); return StatusCode(HttpStatusCode.NoContent); }

        private IHttpActionResult InvalidBody(UpdateFeeRequest body)
        {
            if (!ModelState.IsValid)
            {
                string field = ModelState.First(pair => pair.Value.Errors.Count > 0).Key;
                return Content(HttpStatusCode.BadRequest, new ApiError("INVALID_JSON", "JSON格式或字段类型不正确。", field));
            }
            if (body == null)
                return Content(HttpStatusCode.BadRequest, new ApiError("BODY_REQUIRED", "请提供JSON请求体。", null));
            if (body.ExtraFields?.Count > 0)
                return Content(HttpStatusCode.BadRequest, new ApiError("UNKNOWN_FIELD", "存在不支持的字段，利润和利润率由服务端计算。", body.ExtraFields.Keys.First()));
            return null;
        }
    }
}
