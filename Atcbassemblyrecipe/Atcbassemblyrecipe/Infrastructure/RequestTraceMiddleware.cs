using System.Diagnostics;

namespace Atcbassemblyrecipe.Infrastructure
{
    // One console line per request, so the terminal shows who did what and how it
    // ended. Static files and the browser's favicon poll are skipped - they would
    // bury the lines that matter.
    //
    // Turn the detail up or down with Logging:LogLevel:Atcbassemblyrecipe.Infrastructure
    // ("Information" = writes only, "Debug" = every request including page loads).
    public sealed class RequestTraceMiddleware
    {
        private static readonly string[] IgnoredPrefixes =
        {
            "/css/", "/js/", "/lib/", "/images/", "/uploads/", "/favicon"
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<RequestTraceMiddleware> _logger;

        public RequestTraceMiddleware(RequestDelegate next, ILogger<RequestTraceMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (IgnoredPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            // A write is what people actually want to follow in the terminal, so it
            // is logged at Information; page loads sit at Debug.
            var isWrite = !HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method);

            var user = context.User?.Identity?.Name ?? "anonymous";
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context);
                stopwatch.Stop();

                var level = isWrite ? LogLevel.Information : LogLevel.Debug;
                _logger.Log(
                    level,
                    "{Method} {Path} -> {Status} in {Elapsed}ms (user {User})",
                    context.Request.Method,
                    path,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    user);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{Method} {Path} FAILED after {Elapsed}ms (user {User}): {Message}",
                    context.Request.Method,
                    path,
                    stopwatch.ElapsedMilliseconds,
                    user,
                    ex.Message);
                throw;
            }
        }
    }
}
