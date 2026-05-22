using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/admin-audit")]
[Authorize(Policy = "OwnerOnly")]
public class AdminAuditController(
    MojTerminDbContext dbContext,
    ICurrentBusinessService currentBusinessService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AdminAuditLogDto>>> Get(
        [FromQuery] string? resourceType,
        [FromQuery] string? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 100)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var safeLimit = Math.Clamp(limit, 1, 300);

        var query = dbContext.AdminAuditLogs
            .AsNoTracking()
            .Where(x => x.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            var normalized = resourceType.Trim();
            query = query.Where(x => x.ResourceType == normalized);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var normalized = action.Trim();
            query = query.Where(x => x.Action == normalized);
        }

        if (from.HasValue)
        {
            var fromUtc = from.Value.ToUniversalTime();
            query = query.Where(x => x.CreatedAtUtc >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = to.Value.ToUniversalTime();
            query = query.Where(x => x.CreatedAtUtc <= toUtc);
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(safeLimit)
            .Select(x => new AdminAuditLogDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                ActorUserId = x.ActorUserId,
                ActorName = x.ActorName,
                ActorEmail = x.ActorEmail,
                Action = x.Action,
                ResourceType = x.ResourceType,
                ResourceId = x.ResourceId,
                Summary = x.Summary,
                MetadataJson = x.MetadataJson,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(rows);
    }
}
