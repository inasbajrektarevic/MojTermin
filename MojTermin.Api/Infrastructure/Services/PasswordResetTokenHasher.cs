using System.Security.Cryptography;
using System.Text;

namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Hashes raw password-reset tokens before they hit the DB. Same threat model
/// as <see cref="EmailVerificationTokenHasher"/> and <see cref="RefreshTokenHasher"/>:
/// the raw value travels only inside the reset email; the DB stores the SHA-256
/// digest so a DB dump cannot be replayed against /api/auth/reset-password.
/// </summary>
public static class PasswordResetTokenHasher
{
    public static string Hash(string rawToken)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var digest = SHA256.HashData(bytes);
        return Convert.ToBase64String(digest);
    }
}
