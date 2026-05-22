namespace MojTermin.Api.Domain.Entities;

public class AppUser
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Role { get; set; } = "Owner";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Email verification (Strict mode):
    //  - EmailVerified is the gate. Unverified users cannot complete a login.
    //  - EmailVerificationTokenHash is the SHA-256 of the raw token mailed to the
    //    owner. The raw value never lives in the DB, mirroring how RefreshToken
    //    is stored.
    //  - EmailVerificationTokenExpiresAtUtc enforces a 24-hour window. After that
    //    the user must request a fresh email via /api/auth/resend-verification.
    public bool EmailVerified { get; set; }
    public string? EmailVerificationTokenHash { get; set; }
    public DateTime? EmailVerificationTokenExpiresAtUtc { get; set; }
    public DateTime? EmailVerifiedAtUtc { get; set; }

    // Password reset:
    //  - PasswordResetTokenHash is the SHA-256 of the raw reset token mailed to the
    //    user when they request a reset. The raw value never lives in the DB.
    //  - PasswordResetTokenExpiresAtUtc enforces a short (1-hour) window so a leaked
    //    inbox does not stay a permanent backdoor.
    //  - The token is single-use: completing the reset clears both columns. Issuing
    //    a fresh reset for the same user overwrites them and invalidates any
    //    outstanding link.
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAtUtc { get; set; }

    public Business? Business { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
