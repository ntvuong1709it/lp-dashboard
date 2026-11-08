using LpDashboard.Api.Auth;
using LpDashboard.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

public sealed record PasswordRequest(string Password);

public sealed record ChangePasswordRequest(string Current, string New);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    private string Client => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private string? UserAgent => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    /// <summary>Whether the first-run setup is open and whether this browser is logged in.</summary>
    [HttpGet("status")]
    [ProducesResponseType<AuthStatus>(StatusCodes.Status200OK)]
    public async Task<AuthStatus> Status(CancellationToken ct)
    {
        var status = await auth.GetStatusAsync(SessionCookie.Read(Request), ct);
        if (SessionCookie.Read(Request) is not null && !status.Authenticated)
        {
            SessionCookie.Clear(HttpContext);
        }
        return status;
    }

    /// <summary>Sets the one password (first run, or after a reset) and signs this browser in.</summary>
    [HttpPost("setup")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Setup(PasswordRequest request, CancellationToken ct)
    {
        var outcome = await auth.SetupAsync(request.Password ?? string.Empty, UserAgent, ct);
        return Complete(outcome);
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(PasswordRequest request, CancellationToken ct)
    {
        var outcome = await auth.LoginAsync(request.Password ?? string.Empty, Client, UserAgent, ct);
        return Complete(outcome);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (SessionCookie.Read(Request) is { } token) await auth.LogoutAsync(token, ct);
        SessionCookie.Clear(HttpContext);
        return NoContent();
    }

    /// <summary>Signs out every device, including this one.</summary>
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await auth.LogoutEverywhereAsync(ct);
        SessionCookie.Clear(HttpContext);
        return NoContent();
    }

    /// <summary>Changes the password and signs out all other devices.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var token = SessionCookie.Read(Request);
        if (token is null) return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Login required");

        var error = await auth.ChangePasswordAsync(request.Current ?? string.Empty, request.New ?? string.Empty, token, Client, ct);
        return error switch
        {
            AuthError.None => NoContent(),
            AuthError.WeakPassword => WeakPassword(),
            AuthError.InvalidPassword => Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong password", extensions: Code("wrong_password")),
            AuthError.Throttled => Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many attempts", extensions: Code("throttled")),
            _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cannot change password"),
        };
    }

    private IActionResult Complete(AuthOutcome outcome)
    {
        switch (outcome.Error)
        {
            case AuthError.None:
                SessionCookie.Write(HttpContext, outcome.Token!, outcome.ExpiresAt!.Value);
                return NoContent();
            case AuthError.WeakPassword:
                return WeakPassword();
            case AuthError.SetupNotAllowed:
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "Setup is already done", extensions: Code("setup_closed"));
            case AuthError.SetupRequired:
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Setup required", extensions: Code("setup_required"));
            case AuthError.Throttled:
                var seconds = (int)Math.Ceiling(outcome.RetryAfter?.TotalSeconds ?? 60);
                Response.Headers.RetryAfter = seconds.ToString();
                return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many attempts", detail: $"Try again in {Math.Max(1, seconds / 60)} min.", extensions: Code("throttled"));
            default:
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong password", extensions: Code("wrong_password"));
        }
    }

    private IActionResult WeakPassword() => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Password too short",
        detail: $"Use {AuthService.MinPasswordLength} to {AuthService.MaxPasswordLength} characters.",
        extensions: Code("weak_password"));

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };
}
