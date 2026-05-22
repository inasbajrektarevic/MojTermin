namespace MojTermin.Api.Application.DTOs;

/// <summary>
/// Anonymous read model so the public Angular shell can mirror Auth:AllowPublicRegistration
/// without rebuilding the SPA per environment.
/// </summary>
public sealed class PublicSiteConfigDto
{
    public bool AllowPublicRegistration { get; init; }
}
