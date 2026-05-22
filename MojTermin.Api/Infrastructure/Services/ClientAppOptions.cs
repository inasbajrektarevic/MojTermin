namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Holds the public base URL of the SPA. Used when building deep links that
/// travel out of the API in transactional emails (e.g. the email-verification
/// link in /api/businesses/register). Configured via the "ClientApp" section
/// in appsettings; in production set <c>ClientApp__BaseUrl</c> as an env var.
/// </summary>
public class ClientAppOptions
{
    public const string SectionName = "ClientApp";

    /// <summary>Default points at the local Angular dev server; override per environment.</summary>
    public string BaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Hours a freshly-issued email-verification token stays valid.</summary>
    public int EmailVerificationTokenLifetimeHours { get; set; } = 24;
}
