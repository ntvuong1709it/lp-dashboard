using LpDashboard.Application.Auth;

namespace LpDashboard.Api.Auth;

/// <summary>
/// Gate for the whole API: every <c>/api/**</c> request needs a valid session cookie, except the few auth endpoints used to log in.
/// Also refuses state-changing requests that come from another origin (defence in depth next to SameSite=Lax).
/// <c>/health</c> and the static frontend are outside <c>/api</c> and stay open.
/// </summary>
public sealed class AuthMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private static readonly string[] OpenPaths = ["/api/auth/status", "/api/auth/setup", "/api/auth/login"];
    private readonly HashSet<string> _allowedOrigins = new(
        configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"],
        StringComparer.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context, AuthService auth)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        if (IsStateChanging(request.Method) && !OriginAllowed(request))
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Cross-origin request refused").ExecuteAsync(context);
            return;
        }

        if (OpenPaths.Any(p => request.Path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var ct = context.RequestAborted;
        if (SessionCookie.Read(request) is { } token && await auth.ValidateAsync(token, ct) is { Valid: true } check)
        {
            if (check.RenewedUntil is { } until) SessionCookie.Write(context, token, until);
            await next(context);
            return;
        }

        var setupOpen = await auth.IsSetupOpenAsync(ct);
        await Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: setupOpen ? "Setup required" : "Login required",
            extensions: new Dictionary<string, object?> { ["code"] = setupOpen ? "setup_required" : "unauthenticated" })
            .ExecuteAsync(context);
    }

    private static bool IsStateChanging(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    private bool OriginAllowed(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return true;
        return origin.Equals($"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase) || _allowedOrigins.Contains(origin);
    }
}
