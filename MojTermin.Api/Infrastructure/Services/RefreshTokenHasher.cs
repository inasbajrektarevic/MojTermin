using System.Security.Cryptography;
using System.Text;

namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Hashes raw refresh tokens before they are stored. The raw value is only ever
/// known to the client and to the request that mints/rotates it; the DB sees the
/// digest only, so a DB dump cannot be replayed against the refresh endpoint.
/// </summary>
public static class RefreshTokenHasher
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
