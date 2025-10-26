using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WhatsappClient.Services
{
    public class OutgoingDebugHandler : DelegatingHandler
    {
        private readonly ILogger<OutgoingDebugHandler> _log;
        private readonly IHttpContextAccessor _acc;

        public OutgoingDebugHandler(ILogger<OutgoingDebugHandler> log, IHttpContextAccessor acc)
        {
            _log = log;
            _acc = acc;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var auth = request.Headers.Authorization?.ToString() ?? "(none)";
            var preview = string.IsNullOrEmpty(auth) ? "(none)" :
                          (auth.Length > 32 ? auth.Substring(0, 32) + "..." : auth);

            string empId = "(none)";
            if (request.Headers.TryGetValues("X-Empresa-Id", out var vals))
                empId = vals.FirstOrDefault() ?? "(none)";

            var path = request.RequestUri?.ToString() ?? "(null)";
            _log.LogInformation("CLIENT → {Method} {Path} | Auth={AuthPreview} | X-Empresa-Id={Emp}",
                request.Method, path, preview, empId);

            var res = await base.SendAsync(request, cancellationToken);

            _log.LogInformation("CLIENT ← {Status} {Path}", (int)res.StatusCode, path);
            return res;
        }
    }
}
