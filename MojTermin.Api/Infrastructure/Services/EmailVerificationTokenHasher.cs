using System.Security.Cryptography;
using System.Text;

namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Hashes raw email-verification tokens before they hit the DB. Same threat
/// model as <see cref="RefreshTokenHasher"/>: the raw value travels only inside
/// the verification email; the DB stores the SHA-256 digest, so a DB dump
/// cannot be replayed against /api/auth/verify-email.
/// </summary>
public static class EmailVerificationTokenHasher
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
