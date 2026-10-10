using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dispatcher;
using System.Web.Http.ExceptionHandling;
using Newtonsoft.Json;
using OtherFees.Application;
using OtherFees.Domain;

namespace OtherFees.Api
{
    public static class ApiConfiguration
    {
        public static HttpConfiguration Create(OtherFeeService fees, OtherFeeImportService importer, IDatabaseMaintenance maintenance, bool enableReset)
        {
            var configuration = new HttpConfiguration();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.DateParseHandling = DateParseHandling.None;
            configuration.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Never;
            configuration.Services.Replace(typeof(IHttpControllerActivator), new ControllerFactory(fees, importer, maintenance, enableReset));
            configuration.Services.Replace(typeof(IExceptionHandler), new ApiExceptionHandler());
            configuration.MapHttpAttributeRoutes();
            configuration.EnsureInitialized();
            return configuration;
        }

        private sealed class ControllerFactory : IHttpControllerActivator
        {
            private readonly OtherFeeService fees;
            private readonly OtherFeeImportService importer;
            private readonly IDatabaseMaintenance maintenance;
            private readonly bool enableReset;
            public ControllerFactory(OtherFeeService fees, OtherFeeImportService importer, IDatabaseMaintenance maintenance, bool enableReset)
            { this.fees = fees; this.importer = importer; this.maintenance = maintenance; this.enableReset = enableReset; }
            public IHttpController Create(HttpRequestMessage request, HttpControllerDescriptor descriptor, Type type)
            {
                if (type == typeof(OtherFeesController)) return new OtherFeesController(fees, importer);
                if (type == typeof(SystemController)) return new SystemController(maintenance, enableReset);
                throw new InvalidOperationException("未知控制器。");
            }
        }

        private sealed class ApiExceptionHandler : ExceptionHandler
        {
            public override void Handle(ExceptionHandlerContext context)
            {
                var business = context.Exception as BusinessException;
                HttpStatusCode status;
                ApiError error;
                if (business != null)
                {
                    status = business.Kind == BusinessErrorKind.Conflict ? HttpStatusCode.Conflict
                        : business.Kind == BusinessErrorKind.NotFound ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest;
                    error = new ApiError(business.Code, business.Message, business.Field);
                }
                else
                {
                    // 诊断留在服务端；响应不包含SQL、文件路径或堆栈。
                    Console.Error.WriteLine(context.Exception);
                    status = HttpStatusCode.InternalServerError;
                    error = new ApiError("INTERNAL_ERROR", "服务处理失败，请查看服务端日志。", null);
                }
                context.Result = new System.Web.Http.Results.ResponseMessageResult(context.Request.CreateResponse(status, error));
            }
        }
    }

    public sealed class ApiError
    {
        public string Code { get; }
        public string Message { get; }
        public string Field { get; }
        public ApiError(string code, string message, string field) { Code = code; Message = message; Field = field; }
    }
}
