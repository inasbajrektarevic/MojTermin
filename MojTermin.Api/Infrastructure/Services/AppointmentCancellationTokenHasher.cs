using System.Security.Cryptography;
using System.Text;

namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Hashes raw client-cancellation tokens before they hit the DB. Same threat
/// model as <see cref="EmailVerificationTokenHasher"/> and friends: the raw
/// value is embedded in the confirmation email link; the DB stores the SHA-256
/// digest, so a DB dump cannot be replayed against
/// /api/public/appointments/cancel.
/// </summary>
public static class AppointmentCancellationTokenHasher
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
