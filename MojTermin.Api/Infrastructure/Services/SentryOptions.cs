namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Strongly-typed wrapper around the "Sentry" config section so we can read
/// the DSN once at startup and decide whether to wire the SDK at all.
/// Empty DSN keeps Sentry completely uninitialised (no overhead, no outbound
/// traffic) which is what we want in local development.
/// </summary>
public class SentryOptions
{
    public const string SectionName = "Sentry";

    public string? Dsn { get; set; }

    /// <summary>
    /// Sentry environment tag (e.g. "production", "staging"). Defaults to
    /// ASPNETCORE_ENVIRONMENT when not set.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Fraction of requests sampled for performance traces. 0 disables
    /// tracing, 1 traces everything. Sensible production default is 0.1.
    /// </summary>
    public double TracesSampleRate { get; set; } = 0.1;
}
