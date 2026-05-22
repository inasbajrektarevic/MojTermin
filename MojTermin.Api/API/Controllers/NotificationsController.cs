using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Policy = "OwnerOnly")]
public class NotificationsController(MojTerminDbContext dbContext, ICurrentBusinessService currentBusinessService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<NotificationLogDto>>> Get(
        [FromQuery] NotificationDeliveryStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var safeLimit = Math.Clamp(limit, 1, 200);

        var query = dbContext.NotificationLogs
            .AsNoTracking()
            .Where(x => x.BusinessId == businessId);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
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
            .Select(x => new NotificationLogDto
            {
                Id = x.Id,
                BusinessId = x.BusinessId,
                AppointmentId = x.AppointmentId,
                Channel = x.Channel,
                Status = x.Status,
                Recipient = x.Recipient,
                Subject = x.Subject,
                BodyPreview = x.BodyPreview,
                ErrorMessage = x.ErrorMessage,
                CreatedAtUtc = x.CreatedAtUtc,
                SentAtUtc = x.SentAtUtc
            })
            .ToListAsync();

        return Ok(rows);
    }
}
