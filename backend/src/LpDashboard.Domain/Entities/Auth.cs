namespace LpDashboard.Domain.Entities;

/// <summary>The one password protecting the site (a single row; there is exactly one account).</summary>
public class AuthCredential
{
    public int Id { get; set; }

    /// <summary>PBKDF2 hash in the <c>PBKDF2$sha256$iterations$salt$hash</c> format.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// The <c>Auth:ResetPassword</c> value already used to re-open setup. A reset is armed only while the configured
    /// value differs from this, so a flag left in the config cannot re-open setup on every restart.
    /// </summary>
    public string? ResetNonce { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A logged-in device. Only the SHA-256 of the random cookie token is stored.</summary>
public class AuthSession
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? UserAgent { get; set; }
}
