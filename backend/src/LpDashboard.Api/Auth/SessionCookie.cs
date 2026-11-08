namespace LpDashboard.Api.Auth;

/// <summary>The session cookie: HttpOnly (no script access), Secure whenever the request is HTTPS, SameSite=Lax.</summary>
public static class SessionCookie
{
    public const string Name = "lpdash.session";

    public static string? Read(HttpRequest request) => request.Cookies.TryGetValue(Name, out var token) && token.Length > 0 ? token : null;

    public static void Write(HttpContext context, string token, DateTime expiresUtc) =>
        context.Response.Cookies.Append(Name, token, Options(context, expiresUtc));

    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(Name, Options(context, null));

    private static CookieOptions Options(HttpContext context, DateTime? expiresUtc) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expiresUtc is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null,
        IsEssential = true,
    };
}
