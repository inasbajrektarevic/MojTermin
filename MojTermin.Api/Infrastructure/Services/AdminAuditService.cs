using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.Infrastructure.Services;

public class AdminAuditService(
    MojTerminDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AdminAuditService> logger) : IAdminAuditService
{
    public async Task LogAsync(
        Guid businessId,
        string action,
        string resourceType,
        Guid? resourceId = null,
        string? summary = null,
        object? metadata = null)
    {
        try
        {
            var user = httpContextAccessor.HttpContext?.User;
            var actorId = ParseGuid(
                user?.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                user?.FindFirstValue(ClaimTypes.NameIdentifier));
            var actorName = user?.FindFirstValue(ClaimTypes.Name) ?? "Nepoznat korisnik";
            var actorEmail = user?.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty;

            var row = new AdminAuditLog
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                ActorUserId = actorId,
                ActorName = Truncate(actorName, 150),
                ActorEmail = Truncate(actorEmail, 120),
                Action = Truncate(action, 60),
                ResourceType = Truncate(resourceType, 80),
                ResourceId = resourceId,
                Summary = Truncate(summary, 700),
                MetadataJson = metadata is null ? null : Truncate(JsonSerializer.Serialize(metadata), 4000),
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.AdminAuditLogs.Add(row);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist admin audit log.");
        }
    }

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
