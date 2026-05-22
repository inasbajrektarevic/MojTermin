using Microsoft.Extensions.Configuration;

namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Resolves the business' wall-clock time from UTC. Single-region tenants share
/// one configured timezone (App:TimeZoneId). Both Windows and IANA identifiers
/// are accepted so the same config works on the Windows dev box and in a Linux
/// container.
/// </summary>
public class BusinessTimeProvider
{
    private readonly TimeZoneInfo _timeZone;

    public BusinessTimeProvider(IConfiguration configuration, ILogger<BusinessTimeProvider> logger)
    {
        var configured = configuration["App:TimeZoneId"];
        _timeZone = ResolveZone(configured, logger);
    }

    public TimeZoneInfo TimeZone => _timeZone;

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

    private static TimeZoneInfo ResolveZone(string? configured, ILogger logger)
    {
        // Try the configured id verbatim, then the cross-platform equivalents.
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
        }

        // Always include sensible fallbacks so we never crash if the config is bad.
        candidates.Add("Europe/Sarajevo");
        candidates.Add("Central European Standard Time");
        candidates.Add("CET");

        foreach (var id in candidates)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // try next candidate
            }
            catch (InvalidTimeZoneException)
            {
                // try next candidate
            }
        }

        logger.LogWarning(
            "Could not resolve App:TimeZoneId '{Configured}'. Falling back to system local timezone.",
            configured);
        return TimeZoneInfo.Local;
    }
}
