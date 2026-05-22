namespace MojTermin.Api.Application.Interfaces;

public interface IAdminAuditService
{
    Task LogAsync(
        Guid businessId,
        string action,
        string resourceType,
        Guid? resourceId = null,
        string? summary = null,
        object? metadata = null);
}
