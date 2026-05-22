namespace MojTermin.Api.Infrastructure.Services;

public class AuthOptions
{
    public const string SectionName = "Auth";
    public bool AllowPublicRegistration { get; set; } = false;
    public int RefreshTokenExpirationDays { get; set; } = 30;

    /// <summary>
    /// How long the password-reset link mailed by /api/auth/forgot-password
    /// remains valid. Short by design (1 hour default) so a leaked inbox is
    /// not a permanent backdoor.
    /// </summary>
    public int PasswordResetTokenLifetimeMinutes { get; set; } = 60;
}
